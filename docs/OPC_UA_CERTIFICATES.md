# OPC UA certificate setup

Marks IO uses application certificates to identify both sides of an encrypted OPC UA connection. The protocol mode is fixed to **Basic256Sha256 / SignAndEncrypt**. An anonymous OPC UA user token does not bypass certificate checks. Prepare a separate profile for each instance; the application does not create or approve certificates automatically.

## Profile layout

```text
server-profile/
  certificate.der       own public application certificate, DER
  private_key.pem       matching unencrypted RSA private key, PEM
  writer.der            public certificate of the client allowed to write
  trusted/client.der    same client certificate; other approved readers may be added

client-profile/
  certificate.der       own public application certificate, DER
  private_key.pem       matching unencrypted RSA private key, PEM
  server.der            pinned public certificate of the intended server
  trusted/server.der    same server certificate
```

Select the complete profile directory in the application's **Profil certifikátů** field. `server.der` and `writer.der` must also appear in their profile's `trusted/` directory; their filenames inside `trusted/` are arbitrary. `trusted/` accepts public `.der` and `.pem` files. Never copy the peer's `private_key.pem`.

The **Application URI** entered in each application window must match the URI in that application's own certificate SAN. The server certificate SAN must also contain the DNS name or IP address used in the client's endpoint URL. For example, if the client connects to `opc.tcp://192.168.10.20:4840`, the server certificate needs `IP:192.168.10.20`. If this IP changes, use a stable DNS name covered by the certificate or issue a new certificate.

## Create certificates for a local test

The following commands use OpenSSL in a shell such as WSL. Change the directories, URI, host IP and subject before running them. Keep profiles outside the source repository and restrict access to the private key.

```bash
umask 077
mkdir -p server-profile/trusted client-profile/trusted

openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 \
  -keyout server-profile/private_key.pem -out server-profile/certificate.pem \
  -subj '/CN=Marks-IO-Server' \
  -addext 'subjectAltName=URI:urn:example:marks-io:server,IP:127.0.0.1' \
  -addext 'keyUsage=critical,digitalSignature,nonRepudiation,keyEncipherment,dataEncipherment' \
  -addext 'extendedKeyUsage=serverAuth,clientAuth'
openssl x509 -in server-profile/certificate.pem -outform DER \
  -out server-profile/certificate.der

openssl req -x509 -newkey rsa:3072 -sha256 -nodes -days 365 \
  -keyout client-profile/private_key.pem -out client-profile/certificate.pem \
  -subj '/CN=Marks-IO-Client' \
  -addext 'subjectAltName=URI:urn:example:marks-io:client,DNS:localhost' \
  -addext 'keyUsage=critical,digitalSignature,nonRepudiation,keyEncipherment,dataEncipherment' \
  -addext 'extendedKeyUsage=serverAuth,clientAuth'
openssl x509 -in client-profile/certificate.pem -outform DER \
  -out client-profile/certificate.der

cp client-profile/certificate.der server-profile/writer.der
cp client-profile/certificate.der server-profile/trusted/client.der
cp server-profile/certificate.der client-profile/server.der
cp server-profile/certificate.der client-profile/trusted/server.der
```

Start the server with endpoint `opc.tcp://127.0.0.1:4840`, Application URI `urn:example:marks-io:server` and `server-profile`. Start a second instance as client with the same endpoint, Application URI `urn:example:marks-io:client` and `client-profile`. Add a server node such as namespace URI `urn:example:line1`, NodeId `s=Trigger`, type `Boolean`, value `false` before starting the server. The client must use the same URI, NodeId and value type.

For different machines, replace loopback with a reachable server address and include that address in the server certificate SAN. Approve the peers' SHA-256 fingerprints through an independent channel before adding their public certificates to `trusted/`:

```bash
openssl x509 -in server-profile/certificate.pem -noout -fingerprint -sha256 -dates
openssl x509 -in client-profile/certificate.pem -noout -fingerprint -sha256 -dates
```

## Common connection errors

| Error | Check |
| --- | --- |
| `BadCertificateUntrusted` | The peer's public certificate is present in `trusted/`; restart after changing trust. |
| `BadCertificateHostNameInvalid` | Endpoint DNS/IP matches the server certificate SAN. |
| `BadCertificateUriInvalid` | Each application's configured URI matches its **own** certificate SAN. |
| `BadUserAccessDenied` on write | The client's exact public certificate matches `writer.der` on the server. |
| `BadNodeIdUnknown` | Namespace URI and NodeId match the server node; omit `ns=` in the editor. |
| Connection refused or timeout | Server is running, port is reachable and the firewall permits it. |

A certificate expires at its `notAfter` date. Issue and approve its replacement before expiry, replace the public pin and trust entries on both sides, then restart both modes. Removing trust also requires a restart. Keep private keys outside Git, release ZIPs and logs.
