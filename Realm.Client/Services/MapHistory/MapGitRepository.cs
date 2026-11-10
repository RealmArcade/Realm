using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using LibGit2Sharp;

namespace Realm.Client.Services.MapHistory;

public readonly record struct MapGitSignature(string Name, string Email, DateTimeOffset When);

public readonly record struct MapHistoryRepositorySize(long TotalBytes, long PackedBytes);

public class MapGitCommitInfo
{
	public string Id { get; init; } = string.Empty;
	public string TreeId { get; init; } = string.Empty;
	public string? ParentId { get; init; }
	public DateTimeOffset CommittedAt { get; init; }
	public string Message { get; init; } = string.Empty;
}

public class MapGitRepository
{
	public const string DefaultBranchName = "main";
	public const string BranchReferenceName = "refs/heads/" + DefaultBranchName;
	public const string NotesReferenceName = "refs/notes/commits";

	private const string LooseObjectDirectoryName = "objects";
	private const string PackedObjectDirectoryName = "pack";
	private const string TemporaryPackDirectoryName = "realm-repack";
	private const string VerificationRepositoryDirectoryName = "realm-repack-verify";
	private const int MaximumVerifiedCommitCount = 100000;

	public MapGitRepository(string repositoryDirectory)
	{
		RepositoryDirectory = Path.GetFullPath(repositoryDirectory);
	}

	public string RepositoryDirectory { get; }
	public string ObjectsDirectory => Path.Combine(RepositoryDirectory, LooseObjectDirectoryName);

	public bool Exists
	{
		get
		{
			if (!Directory.Exists(ObjectsDirectory)) return false;
			if (!File.Exists(Path.Combine(RepositoryDirectory, "HEAD"))) return false;
			return Repository.IsValid(RepositoryDirectory);
		}
	}

	public static string ComputeBlobId(ReadOnlySpan<byte> content)
	{
		byte[] header = Encoding.ASCII.GetBytes($"blob {content.Length}\0");
		using var hasher = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA1);
		hasher.AppendData(header);
		hasher.AppendData(content);
		return Convert.ToHexStringLower(hasher.GetHashAndReset());
	}

	public Repository OpenRepositoryScope()
	{
		return OpenRepository();
	}

	public string? ReadHeadCommitId(Repository? openRepository = null)
	{
		if (!Exists) return null;
		if (openRepository != null) return ReadDirectTarget(openRepository, BranchReferenceName);

		using Repository repository = OpenRepository();
		return ReadDirectTarget(repository, BranchReferenceName);
	}

	public void UpdateHead(string commitId, Repository? openRepository = null)
	{
		if (!Exists || string.IsNullOrEmpty(commitId)) return;

		if (openRepository != null)
		{
			UpdateHeadInRepository(openRepository, commitId);
			return;
		}

		using Repository repository = OpenRepository();
		UpdateHeadInRepository(repository, commitId);
	}

	private static void UpdateHeadInRepository(Repository repository, string commitId)
	{
		WriteReference(repository, BranchReferenceName, commitId);
		if (repository.Refs["HEAD"] is not SymbolicReference)
		{
			repository.Refs.UpdateTarget("HEAD", BranchReferenceName);
		}
	}

	public string? ReadVersionLabel(string commitId, Repository? openRepository = null)
	{
		if (!Exists || string.IsNullOrEmpty(commitId)) return null;
		if (openRepository != null) return ReadVersionLabelInRepository(openRepository, commitId);

		using Repository repository = OpenRepository();
		return ReadVersionLabelInRepository(repository, commitId);
	}

	private static string? ReadVersionLabelInRepository(Repository repository, string commitId)
	{
		Note? note = repository.Notes[new ObjectId(commitId)].FirstOrDefault();
		string? message = note?.Message?.Trim();
		return string.IsNullOrEmpty(message) ? null : message;
	}

	public void WriteVersionLabel(string commitId, MapGitSignature signature, string? label)
	{
		if (!Exists || string.IsNullOrEmpty(commitId)) return;

		using Repository repository = OpenRepository();
		var targetObjectId = new ObjectId(commitId);
		var author = new Signature(signature.Name, signature.Email, signature.When);
		Note? existingNote = repository.Notes[targetObjectId].FirstOrDefault();

		if (string.IsNullOrWhiteSpace(label))
		{
			if (existingNote != null)
			{
				repository.Notes.Remove(targetObjectId, author, author, existingNote.Namespace);
			}

			return;
		}

		repository.Notes.Add(targetObjectId, label.Trim(), author, author, existingNote?.Namespace ?? repository.Notes.DefaultNamespace);
	}

	public void ClearVersionLabels()
	{
		if (!Exists) return;

		using Repository repository = OpenRepository();
		if (repository.Refs[NotesReferenceName] != null)
		{
			repository.Refs.Remove(NotesReferenceName);
		}
	}

	public bool TryReadCommit(string commitId, out MapGitCommitInfo commit, Repository? openRepository = null)
	{
		commit = new MapGitCommitInfo();
		if (!Exists || string.IsNullOrEmpty(commitId)) return false;

		if (openRepository != null)
		{
			return TryReadCommitInRepository(openRepository, commitId, out commit);
		}

		using Repository repository = OpenRepository();
		return TryReadCommitInRepository(repository, commitId, out commit);
	}

	private static bool TryReadCommitInRepository(Repository repository, string commitId, out MapGitCommitInfo commit)
	{
		commit = new MapGitCommitInfo();
		Commit? foundCommit = LookupCommit(repository, commitId);
		if (foundCommit == null) return false;

		commit = ToCommitInfo(foundCommit);
		return true;
	}

	public IReadOnlyList<MapGitCommitInfo> ReadVersionHistory(int maximumCount)
	{
		var commits = new List<MapGitCommitInfo>();
		if (!Exists || maximumCount <= 0) return commits;

		using Repository repository = OpenRepository();
		string? commitId = ReadDirectTarget(repository, BranchReferenceName);
		while (!string.IsNullOrEmpty(commitId) && commits.Count < maximumCount)
		{
			Commit? commit = LookupCommit(repository, commitId);
			if (commit == null) break;

			commits.Add(ToCommitInfo(commit));
			commitId = commit.Parents.FirstOrDefault()?.Sha;
		}

		return commits;
	}

	public bool IsHistoryReadable(Repository? openRepository = null)
	{
		if (!Exists) return false;
		if (openRepository != null) return IsHistoryReadableInRepository(openRepository);

		using Repository repository = OpenRepository();
		return IsHistoryReadableInRepository(repository);
	}

	private bool IsHistoryReadableInRepository(Repository repository)
	{
		try
		{
			string? commitId = ReadDirectTarget(repository, BranchReferenceName);
			int visitedCommitCount = 0;
			while (!string.IsNullOrEmpty(commitId) && visitedCommitCount < MaximumVerifiedCommitCount)
			{
				Commit? commit = LookupCommit(repository, commitId);
				if (commit == null) return false;
				if (repository.Lookup<Tree>(new ObjectId(commit.Tree.Sha)) == null) return false;

				visitedCommitCount++;
				commitId = commit.Parents.FirstOrDefault()?.Sha;
			}

			return true;
		}
		catch (Exception exception) when (exception is LibGit2SharpException or ArgumentException or FormatException)
		{
			return false;
		}
	}

	public Dictionary<string, string> ReadTreeFiles(string treeId, Repository? openRepository = null)
	{
		var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (!Exists || string.IsNullOrEmpty(treeId)) return files;

		if (openRepository != null)
		{
			ReadTreeFilesInRepository(openRepository, treeId, files);
			return files;
		}

		using Repository repository = OpenRepository();
		ReadTreeFilesInRepository(repository, treeId, files);
		return files;
	}

	private static void ReadTreeFilesInRepository(Repository repository, string treeId, Dictionary<string, string> files)
	{
		Tree? tree = repository.Lookup<Tree>(new ObjectId(treeId));
		if (tree == null) return;

		CollectTreeFiles(tree, string.Empty, files);
	}

	public bool TryReadBlob(string blobId, out byte[] content, Repository? openRepository = null)
	{
		content = Array.Empty<byte>();
		if (!Exists || string.IsNullOrEmpty(blobId)) return false;

		if (openRepository != null)
		{
			return TryReadBlobInRepository(openRepository, blobId, out content);
		}

		using Repository repository = OpenRepository();
		return TryReadBlobInRepository(repository, blobId, out content);
	}

	private static bool TryReadBlobInRepository(Repository repository, string blobId, out byte[] content)
	{
		content = Array.Empty<byte>();
		Blob? blob = repository.Lookup<Blob>(new ObjectId(blobId));
		if (blob == null) return false;

		using Stream contentStream = blob.GetContentStream();
		using var buffer = new MemoryStream();
		contentStream.CopyTo(buffer);
		content = buffer.ToArray();
		return true;
	}

	public bool HasBlob(string blobId, Repository? openRepository = null)
	{
		if (!Exists || string.IsNullOrEmpty(blobId)) return false;
		if (openRepository != null) return openRepository.ObjectDatabase.Contains(new ObjectId(blobId));

		using Repository repository = OpenRepository();
		return repository.ObjectDatabase.Contains(new ObjectId(blobId));
	}

	public string WriteBlob(byte[] content, Repository? openRepository = null)
	{
		string blobId = ComputeBlobId(content);

		if (openRepository != null)
		{
			return WriteBlobInRepository(openRepository, content, blobId);
		}

		using Repository repository = OpenRepository();
		return WriteBlobInRepository(repository, content, blobId);
	}

	private static string WriteBlobInRepository(Repository repository, byte[] content, string blobId)
	{
		if (repository.ObjectDatabase.Contains(new ObjectId(blobId)))
		{
			return blobId;
		}

		using var contentStream = new MemoryStream(content, false);
		repository.ObjectDatabase.CreateBlob(contentStream);
		return blobId;
	}

	public string WriteTree(IReadOnlyDictionary<string, string> blobIdByPath, Repository? openRepository = null)
	{
		if (openRepository != null)
		{
			return BuildTree(openRepository, blobIdByPath).Sha;
		}

		using Repository repository = OpenRepository();
		return BuildTree(repository, blobIdByPath).Sha;
	}

	public string? FindCommitIdWithTree(string treeId, Repository? openRepository = null)
	{
		if (!Exists || string.IsNullOrEmpty(treeId)) return null;
		if (openRepository != null) return FindCommitIdWithTreeInRepository(openRepository, treeId);

		using Repository repository = OpenRepository();
		return FindCommitIdWithTreeInRepository(repository, treeId);
	}

	private static string? FindCommitIdWithTreeInRepository(Repository repository, string treeId)
	{
		string? commitId = ReadDirectTarget(repository, BranchReferenceName);
		int visitedCommitCount = 0;
		while (!string.IsNullOrEmpty(commitId) && visitedCommitCount < MaximumVerifiedCommitCount)
		{
			Commit? commit = LookupCommit(repository, commitId);
			if (commit == null) return null;
			if (string.Equals(commit.Tree.Sha, treeId, StringComparison.OrdinalIgnoreCase)) return commit.Sha;

			visitedCommitCount++;
			commitId = commit.Parents.FirstOrDefault()?.Sha;
		}

		return null;
	}

	public string WriteCommitForTree(string treeId, string? parentId, MapGitSignature signature, string message)
	{
		using Repository repository = OpenRepository();
		Tree? tree = repository.Lookup<Tree>(new ObjectId(treeId));
		if (tree == null)
		{
			throw new InvalidOperationException($"The stored tree {treeId} could not be read.");
		}

		return CreateCommit(repository, tree, parentId, signature, message);
	}

	public MapHistoryRepositorySize ReadSize()
	{
		if (!Directory.Exists(ObjectsDirectory)) return default;

		long totalBytes = 0;
		long packedBytes = 0;

		foreach (string filePath in Directory.EnumerateFiles(ObjectsDirectory, "*", SearchOption.AllDirectories))
		{
			long length = 0;
			try
			{
				length = new FileInfo(filePath).Length;
			}
			catch (IOException)
			{
				continue;
			}

			totalBytes += length;
			string relativePath = Path.GetRelativePath(ObjectsDirectory, filePath).Replace('\\', '/');

			if (relativePath.StartsWith(PackedObjectDirectoryName + "/", StringComparison.Ordinal) &&
			    relativePath.EndsWith(".pack", StringComparison.Ordinal))
			{
				packedBytes += length;
			}
		}

		return new MapHistoryRepositorySize(totalBytes, packedBytes);
	}

	public MapHistoryRepositorySize SweepUnreachableObjects()
	{
		if (!Exists) return default;

		var tipCommitIds = new List<string>();
		using (Repository repository = OpenRepository())
		{
			string? head = ReadDirectTarget(repository, BranchReferenceName);
			if (!string.IsNullOrEmpty(head)) tipCommitIds.Add(head);

			string? notes = ReadDirectTarget(repository, NotesReferenceName);
			if (!string.IsNullOrEmpty(notes)) tipCommitIds.Add(notes);
		}

		if (tipCommitIds.Count == 0) return ReadSize();

		var reachableCommitIds = new List<string>();
		using (Repository repository = OpenRepository())
		{
			CollectReachableCommitIds(repository, tipCommitIds, reachableCommitIds);
		}

		if (reachableCommitIds.Count == 0) return ReadSize();

		string temporaryPackDirectory = Path.Combine(RepositoryDirectory, TemporaryPackDirectoryName);
		string verificationRepositoryDirectory = Path.Combine(RepositoryDirectory, VerificationRepositoryDirectoryName);
		DeleteDirectoryRecursively(temporaryPackDirectory);
		DeleteDirectoryRecursively(verificationRepositoryDirectory);
		Directory.CreateDirectory(temporaryPackDirectory);

		try
		{
			using (Repository repository = OpenRepository())
			{
				var packOptions = new PackBuilderOptions(temporaryPackDirectory);
				repository.ObjectDatabase.Pack(packOptions, packer =>
				{
					foreach (string reachableCommitId in reachableCommitIds)
					{
						packer.AddRecursively(new ObjectId(reachableCommitId));
					}
				});
			}

			string[] producedPackFiles = Directory.GetFiles(temporaryPackDirectory);
			if (producedPackFiles.Length == 0)
			{
				throw new InvalidOperationException("The history could not be packed.");
			}

			VerifyPackIsSelfContained(verificationRepositoryDirectory, reachableCommitIds, producedPackFiles);
			InstallPackFiles(reachableCommitIds, producedPackFiles);
		}
		finally
		{
			DeleteDirectoryRecursively(temporaryPackDirectory);
			DeleteDirectoryRecursively(verificationRepositoryDirectory);
		}

		return ReadSize();
	}

	private void VerifyPackIsSelfContained(string verificationRepositoryDirectory, List<string> reachableCommitIds, string[] producedPackFiles)
	{
		DeleteDirectoryRecursively(verificationRepositoryDirectory);
		Directory.CreateDirectory(verificationRepositoryDirectory);
		Repository.Init(verificationRepositoryDirectory, true);

		string verificationPackDirectory = Path.Combine(verificationRepositoryDirectory, LooseObjectDirectoryName, PackedObjectDirectoryName);
		Directory.CreateDirectory(verificationPackDirectory);
		foreach (string producedPackFile in producedPackFiles)
		{
			File.Copy(producedPackFile, Path.Combine(verificationPackDirectory, Path.GetFileName(producedPackFile)), true);
		}

		using Repository verificationRepository = new(verificationRepositoryDirectory);
		VerifyReachableObjects(verificationRepository, reachableCommitIds);
	}

	private static void CollectReachableCommitIds(Repository repository, IEnumerable<string> tipCommitIds, List<string> reachableCommitIds)
	{
		var visitedCommitIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var pendingCommitIds = new Queue<string>(tipCommitIds);
		while (pendingCommitIds.Count > 0 && visitedCommitIds.Count < 1_000_000)
		{
			string commitId = pendingCommitIds.Dequeue();
			if (!visitedCommitIds.Add(commitId)) continue;

			Commit? commit = LookupCommit(repository, commitId);
			if (commit == null) continue;

			reachableCommitIds.Add(commit.Sha);
			foreach (Commit parent in commit.Parents)
			{
				pendingCommitIds.Enqueue(parent.Sha);
			}
		}
	}

	private void InstallPackFiles(List<string> tipCommitIds, string[] producedPackFiles)
	{
		string packDirectory = Path.Combine(ObjectsDirectory, PackedObjectDirectoryName);
		Directory.CreateDirectory(packDirectory);

		foreach (string producedPackFile in producedPackFiles)
		{
			File.Move(producedPackFile, Path.Combine(packDirectory, Path.GetFileName(producedPackFile)), true);
		}

		using (Repository repository = OpenRepository())
		{
			VerifyReachableObjects(repository, tipCommitIds);
		}

		PruneSupersededPackFiles(producedPackFiles);
		using (Repository repository = OpenRepository())
		{
			VerifyReachableObjects(repository, tipCommitIds);
		}

		PruneLooseObjectDirectories();
		using (Repository repository = OpenRepository())
		{
			VerifyReachableObjects(repository, tipCommitIds);
		}
	}

	private void PruneSupersededPackFiles(string[] producedPackFiles)
	{
		var producedPackFileNames = new HashSet<string>(StringComparer.Ordinal);
		foreach (string producedPackFile in producedPackFiles)
		{
			producedPackFileNames.Add(Path.GetFileName(producedPackFile));
		}

		string packDirectory = Path.Combine(ObjectsDirectory, PackedObjectDirectoryName);
		if (!Directory.Exists(packDirectory)) return;

		foreach (string packFile in Directory.GetFiles(packDirectory))
		{
			if (producedPackFileNames.Contains(Path.GetFileName(packFile))) continue;
			DeleteFileQuietly(packFile);
		}
	}

	private void PruneLooseObjectDirectories()
	{
		foreach (string looseDirectory in Directory.EnumerateDirectories(ObjectsDirectory))
		{
			if (Path.GetFileName(looseDirectory).Length != 2) continue;
			DeleteDirectoryRecursively(looseDirectory);
		}
	}

	private static void VerifyReachableObjects(Repository repository, List<string> commitIds)
	{
		foreach (string commitId in commitIds)
		{
			Commit? commit = LookupCommit(repository, commitId);
			if (commit == null)
			{
				throw new InvalidOperationException($"Commit {commitId} could not be read after packing.");
			}

			var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			CollectTreeFiles(commit.Tree, string.Empty, files);
			foreach (string blobId in files.Values)
			{
				if (!repository.ObjectDatabase.Contains(new ObjectId(blobId)))
				{
					throw new InvalidOperationException($"Object {blobId} could not be read after packing.");
				}
			}
		}
	}

	private Repository OpenRepository()
	{
		return new Repository(RepositoryDirectory);
	}

	private static string CreateCommit(Repository repository, Tree tree, string? parentId, MapGitSignature signature, string message)
	{
		var parents = new List<Commit>();
		if (!string.IsNullOrEmpty(parentId))
		{
			Commit? parentCommit = LookupCommit(repository, parentId);
			if (parentCommit != null) parents.Add(parentCommit);
		}

		var author = new Signature(signature.Name, signature.Email, signature.When);
		string normalizedMessage = message.EndsWith('\n') ? message : message + "\n";
		Commit commit = repository.ObjectDatabase.CreateCommit(author, author, normalizedMessage, tree, parents, false);
		return commit.Sha;
	}

	private static string? ReadDirectTarget(Repository repository, string referenceName)
	{
		return repository.Refs[referenceName] is DirectReference directReference ? directReference.TargetIdentifier : null;
	}

	private static void WriteReference(Repository repository, string referenceName, string commitId)
	{
		if (repository.Refs[referenceName] is DirectReference existingReference)
		{
			repository.Refs.UpdateTarget(existingReference, new ObjectId(commitId));
			return;
		}

		repository.Refs.Add(referenceName, new ObjectId(commitId));
	}

	private static Commit? LookupCommit(Repository repository, string commitId)
	{
		try
		{
			return repository.Lookup<Commit>(new ObjectId(commitId));
		}
		catch (Exception exception) when (exception is LibGit2SharpException or ArgumentException or FormatException)
		{
			return null;
		}
	}

	private static MapGitCommitInfo ToCommitInfo(Commit commit)
	{
		return new MapGitCommitInfo
		{
			Id = commit.Sha,
			TreeId = commit.Tree.Sha,
			ParentId = commit.Parents.FirstOrDefault()?.Sha,
			CommittedAt = commit.Committer.When,
			Message = commit.Message.TrimEnd('\n')
		};
	}

	private static void CollectTreeFiles(Tree tree, string prefix, Dictionary<string, string> files)
	{
		foreach (TreeEntry entry in tree)
		{
			if (entry.TargetType == TreeEntryTargetType.Tree && entry.Target is Tree childTree)
			{
				CollectTreeFiles(childTree, prefix + entry.Name + "/", files);
			}
			else if (entry.TargetType == TreeEntryTargetType.Blob && entry.Target is Blob blob)
			{
				files[prefix + entry.Name] = blob.Sha;
			}
		}
	}

	private static Tree BuildTree(Repository repository, IReadOnlyDictionary<string, string> blobIdByPath)
	{
		var treeDefinition = new TreeDefinition();
		foreach (var pair in blobIdByPath)
		{
			treeDefinition.Add(pair.Key.Replace('\\', '/'), new ObjectId(pair.Value), Mode.NonExecutableFile);
		}

		return repository.ObjectDatabase.CreateTree(treeDefinition);
	}

	private static void DeleteFileQuietly(string filePath)
	{
		try
		{
			File.SetAttributes(filePath, FileAttributes.Normal);
			File.Delete(filePath);
		}
		catch (IOException exception)
		{
			GameLog.LogError($"[MapGitRepository] Could not remove {filePath}: {exception.Message}");
		}
	}

	private static void DeleteDirectoryRecursively(string directoryPath)
	{
		try
		{
			if (!Directory.Exists(directoryPath)) return;

			foreach (string filePath in Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(filePath, FileAttributes.Normal);
			}

			Directory.Delete(directoryPath, true);
		}
		catch (IOException exception)
		{
			GameLog.LogError($"[MapGitRepository] Could not remove {directoryPath}: {exception.Message}");
		}
	}
}
