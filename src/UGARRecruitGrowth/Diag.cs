using System;
using System.IO;
using BepInEx;

namespace UGARRecruitGrowth
{
	/// <summary>
	/// Small append-only diagnostic log (BepInEx\ugar.recruitgrowth.diag.log). Unlike LogOutput.log it survives a
	/// restart and is written straight to disk, so the last line before a freeze or crash is always there.
	/// </summary>
	internal static class Diag
	{
		static string _path;
		const long MaxBytes = 512 * 1024;

		public static void Write(string msg)
		{
			try
			{
				if (_path == null)
				{
					_path = Path.Combine(Paths.BepInExRootPath, "ugar.recruitgrowth.diag.log");
					var fi = new FileInfo(_path);
					if (fi.Exists && fi.Length > MaxBytes)
						File.Copy(_path, _path + ".old", true);
					if (fi.Exists && fi.Length > MaxBytes)
						File.Delete(_path);
					File.AppendAllText(_path, $"---- {DateTime.Now:yyyy-MM-dd HH:mm:ss} {Plugin.Name} {Plugin.Version} started ----{Environment.NewLine}");
				}
				File.AppendAllText(_path, $"{DateTime.Now:HH:mm:ss.fff} {msg}{Environment.NewLine}");
			}
			catch (Exception)
			{
				// Diagnostics must never break the game.
			}
		}
	}
}
