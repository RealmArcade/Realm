using System.Globalization;
using Realm.Shared.Network;

namespace Realm.ListenServer;

public record ServerArguments(string MapPath, int Port, string Token, string LogPath, string DataPath, int Seed)
{
	public const string MapFlag = ServerArgumentFlags.MapFlag;
	public const string PortFlag = ServerArgumentFlags.PortFlag;
	public const string TokenFlag = ServerArgumentFlags.TokenFlag;
	public const string LogFlag = ServerArgumentFlags.LogFlag;
	public const string DataFlag = ServerArgumentFlags.DataFlag;
	public const string SeedFlag = ServerArgumentFlags.SeedFlag;

	public const string Usage = $"{MapFlag} <absolute map folder> [{PortFlag} <port>] [{SeedFlag} <integer>] [{TokenFlag} <join ticket>] {LogFlag} <log file> [{DataFlag} <folder a map's saved data belongs in>]";

	public static bool TryParse(string[] args, out ServerArguments? arguments, out string error)
	{
		arguments = null;
		error = string.Empty;

		string? mapPath = null;
		string? token = null;
		string? logPath = null;
		string? dataPath = null;
		int port = 0;
		int seed = 0;

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
				case DataFlag:
					if (!TryTakeValue(args, ref index, flag, out dataPath, out error)) return false;
					break;
				case SeedFlag:
					if (!TryTakeValue(args, ref index, flag, out string? seedText, out error)) return false;
					if (!int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
					{
						error = $"{SeedFlag} expects an integer, not '{seedText}'.";
						return false;
					}
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

		if (string.IsNullOrWhiteSpace(logPath))
		{
			error = $"{LogFlag} is required.";
			return false;
		}

		arguments = new ServerArguments(mapPath, port, token ?? string.Empty, logPath, string.IsNullOrWhiteSpace(dataPath) ? AppContext.BaseDirectory : dataPath, seed);
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
