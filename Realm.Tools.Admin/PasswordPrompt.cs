using System.Text;

namespace Realm.Tools.Admin;

public static class PasswordPrompt
{
	public static string Read(string prompt)
	{
		Console.Write(prompt);
		if (Console.IsInputRedirected)
		{
			string? redirectedLine = Console.ReadLine();
			Console.WriteLine();
			return redirectedLine ?? string.Empty;
		}

		var passwordBuilder = new StringBuilder();
		while (true)
		{
			ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
			if (keyInfo.Key == ConsoleKey.Enter)
			{
				Console.WriteLine();
				return passwordBuilder.ToString();
			}

			if (keyInfo.Key == ConsoleKey.Backspace)
			{
				if (passwordBuilder.Length > 0)
				{
					passwordBuilder.Length--;
				}

				continue;
			}

			if (keyInfo.Key == ConsoleKey.Escape)
			{
				passwordBuilder.Clear();
				continue;
			}

			if (!char.IsControl(keyInfo.KeyChar))
			{
				passwordBuilder.Append(keyInfo.KeyChar);
			}
		}
	}
}
