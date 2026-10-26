using System.Globalization;

namespace Realm.ListenServer;

public record ServerArguments(string MapPath, int Port, string Token, string LogPath)
{
	public const string MapFlag = "--map";
	public const string PortFlag = "--port";
	public const string TokenFlag = "--token";
	public const string LogFlag = "--log";

	public const string Usage = $"{MapFlag} <absolute map folder> {PortFlag} <port> {TokenFlag} <token> {LogFlag} <log file>";

	public static bool TryParse(string[] args, out ServerArguments? arguments, out string error)
	{
		arguments = null;
		error = string.Empty;

		string? mapPath = null;
		string? token = null;
		string? logPath = null;
		int port = 0;

		for (int index = 0; index < args.Length; index++)
		{
			string flag = args[index];
			switch (flag)
			{
				case MapFlag:
					if (!TryTakeValue(args, ref index, flag, out mapPath, out error)) return false;
					break;
				case PortFlag:
					if (!TryTakeValue(args, ref index, flag, out string? portText, out error)) return false;
					if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port < 0 || port > 65535)
					{
						error = $"{PortFlag} expects a port between 0 and 65535, not '{portText}'.";
						return false;
					}
					break;
				case TokenFlag:
					if (!TryTakeValue(args, ref index, flag, out token, out error)) return false;
					break;
				case LogFlag:
					if (!TryTakeValue(args, ref index, flag, out logPath, out error)) return false;
					break;
				default:
					error = $"'{flag}' is not a listen server argument.";
					return false;
			}
		}

		if (string.IsNullOrWhiteSpace(mapPath))
		{
			error = $"{MapFlag} is required.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(token))
		{
			error = $"{TokenFlag} is required.";
			return false;
		}

		if (string.IsNullOrWhiteSpace(logPath))
		{
			error = $"{LogFlag} is required.";
			return false;
		}

		arguments = new ServerArguments(mapPath, port, token, logPath);
		return true;
	}

	private static bool TryTakeValue(string[] args, ref int index, string flag, out string? value, out string error)
	{
		value = null;
		error = string.Empty;

		if (index + 1 >= args.Length)
		{
			error = $"{flag} is missing its value.";
			return false;
		}

		index++;
		value = args[index];
		return true;
	}
}
