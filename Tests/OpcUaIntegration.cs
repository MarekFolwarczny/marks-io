using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ModbusServer;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static void CreateProfile(string path, string applicationUri)
{
    Directory.CreateDirectory(Path.Combine(path, "trusted"));
    using var key = RSA.Create(3072);
    var request = new CertificateRequest($"CN={Path.GetFileName(path)}", key,
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var san = new SubjectAlternativeNameBuilder();
    san.AddUri(new Uri(applicationUri));
    san.AddDnsName("localhost");
    san.AddIpAddress(IPAddress.Loopback);
    request.CertificateExtensions.Add(san.Build());
    request.CertificateExtensions.Add(new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation |
        X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment, true));
    request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
        new OidCollection { new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2") }, false));
    using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
    File.WriteAllBytes(Path.Combine(path, "certificate.der"), certificate.Export(X509ContentType.Cert));
    File.WriteAllText(Path.Combine(path, "certificate.pem"), certificate.ExportCertificatePem());
    File.WriteAllText(Path.Combine(path, "private_key.pem"), key.ExportRSAPrivateKeyPem());
}

var root = Path.Combine(Path.GetTempPath(), "OpcUaSimulatorTest-" + Guid.NewGuid().ToString("N"));
var serverProfile = Path.Combine(root, "server");
var writerProfile = Path.Combine(root, "writer");
var readerProfile = Path.Combine(root, "reader");
var outsiderProfile = Path.Combine(root, "outsider");
try
{
    Check((int)OpcUaValue.Parse(OpcUaValueType.Int32, "-123") == -123, "Int32 parse");
    Check(OpcUaValue.Format(true) == "true" && (bool)OpcUaValue.Parse(OpcUaValueType.Boolean, OpcUaValue.Format(true)), "Boolean UI roundtrip");
    Check((string)OpcUaValue.Parse(OpcUaValueType.String, string.Concat(Enumerable.Repeat("😀", 255))) == string.Concat(Enumerable.Repeat("😀", 255)), "Unicode length");
    try { OpcUaValue.Parse(OpcUaValueType.Float, "NaN"); throw new Exception("NaN accepted"); }
    catch (ArgumentException) { }
    try { OpcUaValue.Node("ns=2;s=Trigger", 2); throw new Exception("Namespace index accepted"); }
    catch (ArgumentException) { }

    CreateProfile(serverProfile, "urn:customer:simulator:server");
    CreateProfile(writerProfile, "urn:customer:simulator:writer");
    CreateProfile(readerProfile, "urn:customer:simulator:reader");
    CreateProfile(outsiderProfile, "urn:customer:simulator:outsider");
    var serverCert = Path.Combine(serverProfile, "certificate.der");
    var writerCert = Path.Combine(writerProfile, "certificate.der");
    var readerCert = Path.Combine(readerProfile, "certificate.der");
    File.Copy(writerCert, Path.Combine(serverProfile, "trusted", "writer.der"));
    File.Copy(Path.Combine(readerProfile, "certificate.pem"), Path.Combine(serverProfile, "trusted", "reader.pem"));
    File.Copy(writerCert, Path.Combine(serverProfile, "writer.der"));
    foreach (var profile in new[] { writerProfile, readerProfile, outsiderProfile })
    {
        File.Copy(profile == readerProfile ? Path.Combine(serverProfile, "certificate.pem") : serverCert,
            Path.Combine(profile, "trusted", profile == readerProfile ? "server.pem" : "server.der"));
        File.Copy(serverCert, Path.Combine(profile, "server.der"));
    }
    using var pinned = new X509Certificate2(serverCert);
    OpcUaSecurity.VerifyEndpointHost(pinned, "opc.tcp://127.0.0.1:14840");
    try { OpcUaSecurity.VerifyEndpointHost(pinned, "opc.tcp://example.invalid:14840"); throw new Exception("Wrong host accepted"); }
    catch (InvalidOperationException) { }

    var endpoint = "opc.tcp://127.0.0.1:14840";
    var (config, _) = await OpcUaSecurity.LoadAsync(serverProfile, "urn:customer:simulator:server", true, endpoint);
    using var server = new OpcUaServer("urn:customer:line1",
        [new OpcUaItem("Trigger", "s=Trigger", OpcUaValueType.Boolean, false),
         new OpcUaItem("Count", "i=42", OpcUaValueType.Int32, 12)],
        OpcUaSecurity.PinnedCertificate(serverProfile, "writer.der").RawData, _ => { });
    await server.StartAsync(config, CancellationToken.None);
    try
    {
        using (var writer = new OpcUaClient())
        {
            await writer.ConnectAsync(endpoint, writerProfile, "urn:customer:simulator:writer");
            Check(Equals(await writer.ReadAsync("urn:customer:line1", "s=Trigger"), false), "Initial Boolean read");
            await writer.WriteAsync("urn:customer:line1", "s=Trigger", true);
            await writer.WriteAsync("urn:customer:line1", "i=42", -123);
            Check(Equals(await writer.ReadAsync("urn:customer:line1", "s=Trigger"), true), "Boolean write/read");
            Check(Equals(await writer.ReadAsync("urn:customer:line1", "i=42"), -123), "Int32 write/read");
        }
        using (var reader = new OpcUaClient())
        {
            await reader.ConnectAsync(endpoint, readerProfile, "urn:customer:simulator:reader");
            Check(Equals(await reader.ReadAsync("urn:customer:line1", "i=42"), -123), "Read-only client read");
            try { await reader.WriteAsync("urn:customer:line1", "i=42", 7); throw new Exception("Read-only client wrote data"); }
            catch (Opc.Ua.ServiceResultException ex) when (ex.StatusCode == Opc.Ua.StatusCodes.BadUserAccessDenied) { }
        }
        using (var outsider = new OpcUaClient())
        {
            try
            {
                await outsider.ConnectAsync(endpoint, outsiderProfile, "urn:customer:simulator:outsider");
                throw new Exception("Untrusted client connected");
            }
            catch (Opc.Ua.ServiceResultException ex) when (Opc.Ua.StatusCode.IsBad(ex.StatusCode)) { }
        }
    }
    finally { await server.StopAsync(CancellationToken.None); }
    Console.WriteLine("OPC UA integration PASS");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
