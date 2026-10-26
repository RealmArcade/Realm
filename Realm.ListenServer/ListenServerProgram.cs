using System.Net.Sockets;
using Realm.Shared;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public static class ListenServerProgram
{
	private const int ExitSuccess = 0;
	private const int ExitUsage = 64;
	private const int ExitMapUnavailable = 66;
	private const int ExitInternalError = 70;
	private const int ExitIoError = 74;

	public static int Main(string[] args)
	{
		if (!ServerArguments.TryParse(args, out ServerArguments? arguments, out string argumentError))
		{
			Console.Error.WriteLine(argumentError);
			Console.Error.WriteLine($"usage: Realm.ListenServer {ServerArguments.Usage}");
			return ExitUsage;
		}

		using var log = new ServerLog(arguments!.LogPath);
		log.Write($"starting pid={Environment.ProcessId} map={arguments.MapPath} port={arguments.Port} protocol={RealmProtocol.Version} build={RealmVersion.GameBuildNumber}");

		using var host = new ListenServerHost(arguments, log);
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			log.Write("interrupt received");
			host.Stop();
		};

		try
		{
			host.Start();
		}
		catch (DirectoryNotFoundException exception)
		{
			log.Write(exception.Message);
			return ExitMapUnavailable;
		}
		catch (SocketException exception)
		{
			log.Write($"could not listen on port {arguments.Port}: {exception.Message}");
			return ExitIoError;
		}
		catch (UnauthorizedAccessException exception)
		{
			log.Write($"could not listen on port {arguments.Port}: {exception.Message}");
			return ExitIoError;
		}
		catch (Exception exception)
		{
			log.Write($"failed to start: {exception}");
			return ExitInternalError;
		}

		host.WaitForShutdown();
		host.Stop();
		log.Write("stopped");
		return ExitSuccess;
	}
}
