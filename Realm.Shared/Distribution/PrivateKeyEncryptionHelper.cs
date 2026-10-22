using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;
using Realm.Shared.Metadata;
using Zxcvbn;

namespace Realm.Shared.Distribution;

public static class PrivateKeyEncryptionHelper
{
	public const string AlgorithmName = "Argon2id-XChaCha20Poly1305";
	public const int MinimumPasswordLength = 12;
	public const int MaximumPasswordLength = 128;
	public const int MinimumPasswordScore = 3;
	public const long DefaultMemorySizeKibibytes = 65536;
	public const long DefaultNumberOfPasses = 3;
	public const int DefaultDegreeOfParallelism = 1;

	private static readonly AeadAlgorithm Aead = AeadAlgorithm.XChaCha20Poly1305;

	public static AuthorshipKeyEncryption EncryptPrivateKey(string privateKeyBase64, string password)
	{
		if (!TryValidatePassword(password, out string passwordError))
		{
			throw new ArgumentException(passwordError, nameof(password));
		}

		byte[] rawPrivateKeyBytes = Convert.FromBase64String(privateKeyBase64);
		try
		{
			return EncryptPrivateKey(rawPrivateKeyBytes, password);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(rawPrivateKeyBytes);
		}
	}

	private static AuthorshipKeyEncryption EncryptPrivateKey(ReadOnlySpan<byte> rawPrivateKeyBytes, string password)
	{
		byte[] salt = RandomNumberGenerator.GetBytes(GetArgon2idAlgorithm().MinSaltSize);
		byte[] nonce = RandomNumberGenerator.GetBytes(Aead.NonceSize);
		byte[] passwordBytes = GetNormalizedPasswordBytes(password);

		try
		{
			using var encryptionKey = DeriveEncryptionKey(passwordBytes, salt);
			byte[] ciphertextBytes = Aead.Encrypt(encryptionKey, nonce, ReadOnlySpan<byte>.Empty, rawPrivateKeyBytes);

			return new AuthorshipKeyEncryption
			{
				Algorithm = AlgorithmName,
				MemorySizeKibibytes = DefaultMemorySizeKibibytes,
				NumberOfPasses = DefaultNumberOfPasses,
				DegreeOfParallelism = DefaultDegreeOfParallelism,
				SaltBase64 = Convert.ToBase64String(salt),
				NonceBase64 = Convert.ToBase64String(nonce),
				CiphertextBase64 = Convert.ToBase64String(ciphertextBytes)
			};
		}
		finally
		{
			CryptographicOperations.ZeroMemory(passwordBytes);
		}
	}

	public static byte[] DecryptPrivateKeyBytes(AuthorshipKeyEncryption encryption, string password)
	{
		if (!string.Equals(encryption.Algorithm, AlgorithmName, StringComparison.Ordinal))
		{
			throw new PrivateKeyDecryptionException($"Unsupported private key encryption algorithm '{encryption.Algorithm}'.");
		}

		byte[] salt;
		byte[] nonce;
		byte[] ciphertextBytes;
		try
		{
			salt = Convert.FromBase64String(encryption.SaltBase64);
			nonce = Convert.FromBase64String(encryption.NonceBase64);
			ciphertextBytes = Convert.FromBase64String(encryption.CiphertextBase64);
		}
		catch (FormatException)
		{
			throw new PrivateKeyDecryptionException("The encrypted private key is malformed.");
		}

		if (nonce.Length != Aead.NonceSize || ciphertextBytes.Length <= Aead.TagSize)
		{
			throw new PrivateKeyDecryptionException("The encrypted private key is malformed.");
		}

		byte[] passwordBytes = GetNormalizedPasswordBytes(password);
		try
		{
			using var encryptionKey = DeriveEncryptionKey(passwordBytes, salt, encryption);
			byte[]? plaintextBytes = Aead.Decrypt(encryptionKey, nonce, ReadOnlySpan<byte>.Empty, ciphertextBytes);
			if (plaintextBytes == null)
			{
				throw new PrivateKeyDecryptionException("Failed to decrypt the private key. The password is incorrect or the key file has been tampered with.");
			}

			return plaintextBytes;
		}
		catch (ArgumentException)
		{
			throw new PrivateKeyDecryptionException("Failed to decrypt the private key. The password is incorrect or the key file has been tampered with.");
		}
		catch (CryptographicException)
		{
			throw new PrivateKeyDecryptionException("Failed to decrypt the private key. The password is incorrect or the key file has been tampered with.");
		}
		finally
		{
			CryptographicOperations.ZeroMemory(passwordBytes);
		}
	}

	public static bool TryValidatePassword(string? password, out string errorMessage)
	{
		errorMessage = string.Empty;
		if (string.IsNullOrEmpty(password))
		{
			errorMessage = "A password is required to encrypt the private key.";
			return false;
		}

		string normalizedPassword = NormalizePassword(password);
		if (normalizedPassword.Length < MinimumPasswordLength)
		{
			errorMessage = $"Password must be at least {MinimumPasswordLength} characters long. Use a long passphrase with spaces and mixed words.";
			return false;
		}

		if (normalizedPassword.Length > MaximumPasswordLength)
		{
			errorMessage = $"Password must be at most {MaximumPasswordLength} characters long.";
			return false;
		}

		Result strength = Core.EvaluatePassword(normalizedPassword);
		if (strength.Score < MinimumPasswordScore)
		{
			errorMessage = $"{BuildStrengthFeedback(strength)} Choose a longer, less predictable passphrase (score {strength.Score} of 4, at least {MinimumPasswordScore} required).";
			return false;
		}

		return true;
	}

	public static string NormalizePassword(string password)
	{
		return password.Normalize(NormalizationForm.FormC);
	}

	private static string BuildStrengthFeedback(Result strength)
	{
		var feedbackParts = new List<string>();
		if (!string.IsNullOrWhiteSpace(strength.Feedback?.Warning))
		{
			feedbackParts.Add(strength.Feedback.Warning);
		}

		if (strength.Feedback?.Suggestions != null)
		{
			feedbackParts.AddRange(strength.Feedback.Suggestions.Where(suggestion => !string.IsNullOrWhiteSpace(suggestion)));
		}

		if (feedbackParts.Count == 0)
		{
			return "Password is too weak or predictable.";
		}

		return string.Join(" ", feedbackParts);
	}

	private static byte[] GetNormalizedPasswordBytes(string password)
	{
		return Encoding.UTF8.GetBytes(NormalizePassword(password));
	}

	private static Key DeriveEncryptionKey(ReadOnlySpan<byte> passwordBytes, ReadOnlySpan<byte> salt, AuthorshipKeyEncryption? encryption = null)
	{
		var parameters = new Argon2Parameters
		{
			MemorySize = encryption?.MemorySizeKibibytes ?? DefaultMemorySizeKibibytes,
			NumberOfPasses = encryption?.NumberOfPasses ?? DefaultNumberOfPasses,
			DegreeOfParallelism = encryption?.DegreeOfParallelism ?? DefaultDegreeOfParallelism
		};

		return PasswordBasedKeyDerivationAlgorithm.Argon2id(parameters).DeriveKey(passwordBytes, salt, Aead);
	}

	private static Argon2id GetArgon2idAlgorithm()
	{
		return PasswordBasedKeyDerivationAlgorithm.Argon2id(new Argon2Parameters
		{
			MemorySize = DefaultMemorySizeKibibytes,
			NumberOfPasses = DefaultNumberOfPasses,
			DegreeOfParallelism = DefaultDegreeOfParallelism
		});
	}
}

public class PrivateKeyDecryptionException : Exception
{
	public PrivateKeyDecryptionException(string message) : base(message)
	{
	}
}
