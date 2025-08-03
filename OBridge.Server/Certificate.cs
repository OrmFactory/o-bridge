using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using OBridge.Server.Config;

namespace OBridge.Server;

public class Certificate
{
	public static X509Certificate2 GenerateSelfSignedCertificate(string subjectName, string password = "")
	{
		var distinguishedName = new X500DistinguishedName($"CN={subjectName}");

		using var rsa = RSA.Create(2048);
		var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

		request.CertificateExtensions.Add(
			new X509EnhancedKeyUsageExtension(
				new OidCollection {
					new Oid("1.3.6.1.5.5.7.3.1")
				}, false));

		request.CertificateExtensions.Add(
			new X509KeyUsageExtension(
				X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
				false));

		request.CertificateExtensions.Add(
			new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

		var now = DateTimeOffset.UtcNow;
		var cert = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(5));
		return cert;
	}

	public static X509Certificate2 LoadCertificate(Settings settings, ILogger logger)
	{
		var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
		var certFile = settings.CertificatePath;
		if (certFile == null)
		{
			certFile = "certs/default.pfx";
			logger.LogInformation("No cert file in settings, using certs/default.pfx");
		}

		if (!Path.IsPathRooted(certFile))
			certFile = Path.Combine(dir.FullName, certFile);

		if (!File.Exists(certFile))
		{
			var info = "Cert file does not exist: " + certFile;

			if (settings.CertificatePath != null)
				throw new FileNotFoundException(info);

			logger.LogInformation(info);
			logger.LogInformation("Generating new certificate");

			var cert = GenerateSelfSignedCertificate("localhost");
			var export = cert.Export(X509ContentType.Pfx, "");

			var certDir = Path.GetDirectoryName(certFile);
			if (!string.IsNullOrEmpty(certDir))
				Directory.CreateDirectory(certDir);

			File.WriteAllBytes(certFile, export);
			return cert;
		}

		return new X509Certificate2(certFile, "");
	}
}