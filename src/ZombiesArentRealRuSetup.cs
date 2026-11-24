using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Zombies Aren't Real - Русификатор")]
[assembly: AssemblyDescription("Установщик русификатора текста и шрифтов для Zombies Aren't Real")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("neoru-droid")]
[assembly: AssemblyProduct("Zombies Aren't Real - Русификатор")]
[assembly: AssemblyCopyright("Copyright (c) 2026 neoru-droid")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace ZombiesArentRealRuSetup
{
    internal static class Program
    {
        public const int TargetAppId = 5185890;
        public const string SupportUrl = "https://pay.cloudtips.ru/p/240b8760";
        public const string BoostyUrl = "https://boosty.to/cd_tixo";
        public const string GithubUrl = "https://github.com/neoru-droid/zombies-arent-real-ru";

        private const int AttachParentProcess = -1;
        private const int StdOutputHandle = -11;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [STAThread]
        private static int Main(string[] args)
        {
            ConfigureTls();
            string appBaseDir = SafeTrimTrailingSlash(AppDomain.CurrentDomain.BaseDirectory);

            CliArguments cli;
            string parseError;
            if (!CliArguments.TryParse(args, out cli, out parseError))
            {
                EnsureConsoleAttached();
                ConfigureUtf8StdStreams();
                Console.Error.WriteLine("ОШИБКА: " + parseError);
                PrintCliHelp();
                return 1;
            }

            if (cli.ShowHelp)
            {
                EnsureConsoleAttached();
                ConfigureUtf8StdStreams();
                PrintCliHelp();
                return 0;
            }

            if (cli.Mode == CliMode.Status || cli.Mode == CliMode.Install || cli.Mode == CliMode.Uninstall)
            {
                return RunHeadlessCli(appBaseDir, cli);
            }

            try
            {
                SetProcessDPIAware();
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            PayloadData payload;
            try
            {
                payload = PayloadLoader.LoadEmbeddedPayload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось загрузить встроенные ресурсы русификатора:\n" + ex.Message,
                    "Zombies Aren't Real — Ошибка",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return 1;
            }

            Application.Run(new SetupForm(payload, appBaseDir, cli.ExplicitGameDir, cli.ExplicitSteamDir, cli.Mode));
            return 0;
        }

        private static int RunHeadlessCli(string appBaseDir, CliArguments cli)
        {
            EnsureConsoleAttached();
            ConfigureUtf8StdStreams();

            try
            {
                PayloadData payload = PayloadLoader.LoadEmbeddedPayload();

                if (cli.Mode == CliMode.Status)
                {
                    return InstallerService.RunStatusCli(payload, appBaseDir, cli.ExplicitGameDir, cli.ExplicitSteamDir);
                }
                if (cli.Mode == CliMode.Install)
                {
                    string steamDir = SteamLocator.DetectSteamDirectory(cli.ExplicitSteamDir, cli.ExplicitGameDir);
                    string gameDir = SteamLocator.ResolveRequiredGameDirectory(cli.ExplicitGameDir, steamDir, appBaseDir);
                    if (string.IsNullOrEmpty(steamDir))
                    {
                        steamDir = SteamLocator.DetectSteamDirectory(null, gameDir);
                    }
                    InstallerService.Install(payload, gameDir, steamDir, Console.WriteLine);
                    return 0;
                }
                if (cli.Mode == CliMode.Uninstall)
                {
                    string steamDir = SteamLocator.DetectSteamDirectory(cli.ExplicitSteamDir, cli.ExplicitGameDir);
                    string gameDir = SteamLocator.ResolveRequiredGameDirectory(cli.ExplicitGameDir, steamDir, appBaseDir);
                    if (string.IsNullOrEmpty(steamDir))
                    {
                        steamDir = SteamLocator.DetectSteamDirectory(null, gameDir);
                    }
                    InstallerService.Uninstall(gameDir, steamDir, Console.WriteLine);
                    return 0;
                }
                return 1;
            }
            catch (UnauthorizedAccessException uaEx)
            {
                Console.Error.WriteLine("ОШИБКА: Отказано в доступе к файлам игры (" + uaEx.Message + "). Запустите установщик или терминал от имени администратора.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ОШИБКА: " + ex.Message);
                return 1;
            }
        }

        public static void ConfigureTls()
        {
            try
            {
                ServicePointManager.DefaultConnectionLimit = 16;
                ServicePointManager.Expect100Continue = false;
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)768 | (SecurityProtocolType)3072;
            }
            catch
            {
            }
        }

        public static void OpenBrowserUrl(string url)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Не удалось открыть ссылку в браузере:\n" + url + "\n\n" + ex.Message,
                    "Открытие ссылки",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        public static string SafeTrimTrailingSlash(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }
            if (path.Length <= 3 && path.EndsWith(":\\", StringComparison.Ordinal))
            {
                return path;
            }
            return path.TrimEnd('\\', '/');
        }

        private static void EnsureConsoleAttached()
        {
            try
            {
                IntPtr stdOut = GetStdHandle(StdOutputHandle);
                if (stdOut == IntPtr.Zero)
                {
                    AttachConsole(AttachParentProcess);
                }
            }
            catch
            {
            }
        }

        private static void ConfigureUtf8StdStreams()
        {
            UTF8Encoding utf8NoBom = new UTF8Encoding(false);
            try
            {
                Console.OutputEncoding = utf8NoBom;
            }
            catch
            {
            }

            try
            {
                Stream rawOut = Console.OpenStandardOutput();
                if (rawOut != null && rawOut != Stream.Null)
                {
                    StreamWriter outWriter = new StreamWriter(rawOut, utf8NoBom);
                    outWriter.AutoFlush = true;
                    Console.SetOut(outWriter);
                }
            }
            catch
            {
            }

            try
            {
                Stream rawErr = Console.OpenStandardError();
                if (rawErr != null && rawErr != Stream.Null)
                {
                    StreamWriter errWriter = new StreamWriter(rawErr, utf8NoBom);
                    errWriter.AutoFlush = true;
                    Console.SetError(errWriter);
                }
            }
            catch
            {
            }
        }

        private static void PrintCliHelp()
        {
            Console.WriteLine("Zombies Aren't Real - Русификатор v1.3 (neoru-droid)");
            Console.WriteLine();
            Console.WriteLine("Использование:");
            Console.WriteLine("  ZombiesArentReal-RU-Setup.exe                        Графическое окно установщика");
            Console.WriteLine("  ZombiesArentReal-RU-Setup.exe --status [параметры]   Проверка состояния и путей");
            Console.WriteLine("  ZombiesArentReal-RU-Setup.exe --install [параметры]  Установка русификатора");
            Console.WriteLine("  ZombiesArentReal-RU-Setup.exe --uninstall [параметры] Возврат оригинальных файлов");
            Console.WriteLine();
            Console.WriteLine("Параметры:");
            Console.WriteLine("  --game-dir <путь>   Путь к папке Zombies-Arent-Real");
            Console.WriteLine("  --steam-dir <путь>  Путь к папке клиента Steam");
        }
    }

    internal enum CliMode
    {
        DefaultGui,
        UacInstallGui,
        UacUninstallGui,
        Status,
        Install,
        Uninstall
    }

    internal sealed class CliArguments
    {
        public CliMode Mode = CliMode.DefaultGui;
        public bool ShowHelp;
        public string ExplicitGameDir;
        public string ExplicitSteamDir;

        public static bool TryParse(string[] args, out CliArguments parsed, out string error)
        {
            parsed = new CliArguments();
            error = null;
            if (args == null || args.Length == 0)
            {
                return true;
            }

            int modeCount = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i] ?? string.Empty;
                if (EqualsFlag(arg, "--help") || EqualsFlag(arg, "-h") || EqualsFlag(arg, "/?"))
                {
                    parsed.ShowHelp = true;
                }
                else if (EqualsFlag(arg, "--status"))
                {
                    parsed.Mode = CliMode.Status;
                    modeCount++;
                }
                else if (EqualsFlag(arg, "--install"))
                {
                    parsed.Mode = CliMode.Install;
                    modeCount++;
                }
                else if (EqualsFlag(arg, "--uninstall") || EqualsFlag(arg, "--restore"))
                {
                    parsed.Mode = CliMode.Uninstall;
                    modeCount++;
                }
                else if (EqualsFlag(arg, "--uac-install"))
                {
                    parsed.Mode = CliMode.UacInstallGui;
                    modeCount++;
                }
                else if (EqualsFlag(arg, "--uac-uninstall"))
                {
                    parsed.Mode = CliMode.UacUninstallGui;
                    modeCount++;
                }
                else if (EqualsFlag(arg, "--game-dir"))
                {
                    if (i + 1 >= args.Length)
                    {
                        error = "После флага --game-dir необходимо указать путь к папке игры.";
                        return false;
                    }
                    parsed.ExplicitGameDir = args[++i];
                }
                else if (arg.StartsWith("--game-dir=", StringComparison.OrdinalIgnoreCase))
                {
                    parsed.ExplicitGameDir = arg.Substring("--game-dir=".Length);
                }
                else if (EqualsFlag(arg, "--steam-dir"))
                {
                    if (i + 1 >= args.Length)
                    {
                        error = "После флага --steam-dir необходимо указать путь к папке Steam.";
                        return false;
                    }
                    parsed.ExplicitSteamDir = args[++i];
                }
                else if (arg.StartsWith("--steam-dir=", StringComparison.OrdinalIgnoreCase))
                {
                    parsed.ExplicitSteamDir = arg.Substring("--steam-dir=".Length);
                }
                else
                {
                    error = "Неизвестный аргумент командной строки: " + arg;
                    return false;
                }
            }

            if (!parsed.ShowHelp && modeCount > 1)
            {
                error = "Укажите только один режим работы (--status, --install, или --uninstall).";
                return false;
            }

            return true;
        }

        private static bool EqualsFlag(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal sealed class PayloadData
    {
        public string Version;
        public int AppId;
        public Dictionary<string, string> Lang;
        public Dictionary<string, byte[]> Fonts;
    }

    internal static class PayloadLoader
    {
        private const string EmbeddedResourceName = "zar_ru_payload.gz";

        public static PayloadData LoadEmbeddedPayload()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            Stream resStream = asm.GetManifestResourceStream(EmbeddedResourceName);
            if (resStream == null)
            {
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith("ru_payload.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        resStream = asm.GetManifestResourceStream(names[i]);
                        break;
                    }
                }
            }
            if (resStream == null)
            {
                throw new InvalidDataException("Встроенный ресурс zar_ru_payload.gz не найден в исполняемом файле.");
            }

            using (resStream)
            {
                return LoadFromGzipStream(resStream);
            }
        }

        public static PayloadData LoadFromGzipStream(Stream gzStream)
        {
            string jsonText;
            using (GZipStream gz = new GZipStream(gzStream, CompressionMode.Decompress))
            using (StreamReader reader = new StreamReader(gz, Encoding.UTF8))
            {
                jsonText = reader.ReadToEnd();
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            serializer.RecursionLimit = 64;

            IDictionary<string, object> root = serializer.DeserializeObject(jsonText) as IDictionary<string, object>;
            if (root == null)
            {
                throw new InvalidDataException("Некорректный JSON внутри zar_ru_payload.gz.");
            }

            PayloadData data = new PayloadData();
            data.Version = GetString(root, "version", "1.1.0");
            data.AppId = GetInt(root, "app_id", Program.TargetAppId);
            data.Lang = ParseLangMap(root);
            data.Fonts = ParseFontsMap(root);
            return data;
        }

        private static Dictionary<string, string> ParseLangMap(IDictionary<string, object> root)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            IDictionary<string, object> rawLang = GetDict(root, "lang");
            if (rawLang == null)
            {
                throw new InvalidDataException("В ресурсе русификатора отсутствует секция lang.");
            }
            foreach (KeyValuePair<string, object> kv in rawLang)
            {
                map[kv.Key] = kv.Value == null ? string.Empty : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            }
            return map;
        }

        private static Dictionary<string, byte[]> ParseFontsMap(IDictionary<string, object> root)
        {
            Dictionary<string, byte[]> fonts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            IDictionary<string, object> rawFonts = GetDict(root, "fonts");
            if (rawFonts == null)
            {
                throw new InvalidDataException("В ресурсе русификатора отсутствует секция fonts.");
            }

            foreach (KeyValuePair<string, object> kv in rawFonts)
            {
                string b64 = kv.Value as string;
                if (!string.IsNullOrEmpty(b64))
                {
                    fonts[kv.Key] = Convert.FromBase64String(b64);
                }
            }
            return fonts;
        }

        private static IDictionary<string, object> GetDict(IDictionary<string, object> parent, string key)
        {
            object val;
            if (parent.TryGetValue(key, out val))
            {
                return val as IDictionary<string, object>;
            }
            return null;
        }

        private static string GetString(IDictionary<string, object> dict, string key, string fallback)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                return Convert.ToString(val, CultureInfo.InvariantCulture);
            }
            return fallback;
        }

        private static int GetInt(IDictionary<string, object> dict, string key, int fallback)
        {
            object val;
            if (dict.TryGetValue(key, out val) && val != null)
            {
                return Convert.ToInt32(val, CultureInfo.InvariantCulture);
            }
            return fallback;
        }
    }

    internal static class SteamLocator
    {
        public const string DefaultGameFolderName = "Zombies-Arent-Real";
        public const string GameExeName = "zombies-arent-real.exe";
        public const string SchemaFileName = "UserGameStatsSchema_5185890.bin";

        public static bool IsValidGameDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return false;
            }
            try
            {
                string exePath = Path.Combine(dir, GameExeName);
                string enLangPath = Path.Combine(dir, @"lang\en.lang");
                return File.Exists(exePath) && File.Exists(enLangPath);
            }
            catch
            {
                return false;
            }
        }

        public static string NormalizePickedGameDirectory(string pickedDir)
        {
            if (string.IsNullOrWhiteSpace(pickedDir))
            {
                return null;
            }
            string clean = Program.SafeTrimTrailingSlash(pickedDir.Trim().Trim('"'));
            if (IsValidGameDirectory(clean))
            {
                return SafeFullPath(clean);
            }
            try
            {
                string parent = Path.GetDirectoryName(clean);
                if (!string.IsNullOrEmpty(parent) && IsValidGameDirectory(parent))
                {
                    return SafeFullPath(parent);
                }
            }
            catch
            {
            }
            return clean;
        }

        public static string DetectSteamDirectory(string explicitSteamDir, string hintGameDir)
        {
            if (!string.IsNullOrWhiteSpace(explicitSteamDir))
            {
                string trimmed = Program.SafeTrimTrailingSlash(explicitSteamDir.Trim().Trim('"'));
                if (SafeDirectoryExists(trimmed))
                {
                    return SafeFullPath(trimmed);
                }
                return null;
            }

            List<string> candidates = new List<string>();
            AddRegistrySteamCandidates(candidates);
            candidates.Add(@"C:\Program Files (x86)\Steam");
            candidates.Add(@"C:\Program Files\Steam");
            AddProtonSteamCandidates(candidates);

            if (!string.IsNullOrWhiteSpace(hintGameDir))
            {
                TryAddSteamRootFromGameDir(candidates, hintGameDir);
            }

            List<string> driveRoots = GetSafeLocalDriveRoots();
            for (int i = 0; i < driveRoots.Count; i++)
            {
                candidates.Add(Path.Combine(driveRoots[i], "Steam"));
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                string cand = candidates[i];
                if (SafeDirectoryExists(cand))
                {
                    string statsDir = Path.Combine(cand, @"appcache\stats");
                    string steamExe = Path.Combine(cand, "steam.exe");
                    string steamSh = Path.Combine(cand, "steam.sh");
                    string vdf = Path.Combine(cand, @"steamapps\libraryfolders.vdf");
                    if (SafeDirectoryExists(statsDir) || SafeFileExists(steamExe) || SafeFileExists(steamSh) || SafeFileExists(vdf))
                    {
                        return SafeFullPath(cand);
                    }
                }
            }
            return null;
        }

        public static string ResolveSteamStatsDirectory(string steamDir)
        {
            if (string.IsNullOrWhiteSpace(steamDir))
            {
                return null;
            }
            string nestedStats = Path.Combine(steamDir, @"appcache\stats");
            if (SafeDirectoryExists(nestedStats))
            {
                return nestedStats;
            }
            if (SafeFileExists(Path.Combine(steamDir, SchemaFileName))
                || steamDir.EndsWith(@"\appcache\stats", StringComparison.OrdinalIgnoreCase))
            {
                return steamDir;
            }
            return nestedStats;
        }

        public static string AutoDetectGameDirectory(string steamDir, string appBaseDir)
        {
            List<string> candidates = new List<string>();

            if (!string.IsNullOrWhiteSpace(steamDir))
            {
                AddLibraryCandidates(candidates, steamDir);
                AddVdfLibraryCandidates(candidates, Path.Combine(steamDir, @"steamapps\libraryfolders.vdf"));
            }

            List<string> regRoots = new List<string>();
            AddRegistrySteamCandidates(regRoots);
            for (int i = 0; i < regRoots.Count; i++)
            {
                AddLibraryCandidates(candidates, regRoots[i]);
                AddVdfLibraryCandidates(candidates, Path.Combine(regRoots[i], @"steamapps\libraryfolders.vdf"));
            }

            AddLibraryCandidates(candidates, @"C:\Program Files (x86)\Steam");
            AddVdfLibraryCandidates(candidates, @"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf");
            AddProtonLibraryCandidates(candidates);

            List<string> driveRoots = GetSafeLocalDriveRoots();
            for (int i = 0; i < driveRoots.Count; i++)
            {
                string root = driveRoots[i];
                string steamLib = Path.Combine(root, "SteamLibrary");
                string steamRoot = Path.Combine(root, "Steam");
                string gamesSteam = Path.Combine(root, @"Games\Steam");

                AddLibraryCandidates(candidates, steamLib);
                AddVdfLibraryCandidates(candidates, Path.Combine(steamLib, @"steamapps\libraryfolders.vdf"));
                AddLibraryCandidates(candidates, steamRoot);
                AddVdfLibraryCandidates(candidates, Path.Combine(steamRoot, @"steamapps\libraryfolders.vdf"));
                AddLibraryCandidates(candidates, gamesSteam);
                candidates.Add(Path.Combine(root, @"Games\" + DefaultGameFolderName));
            }

            if (!string.IsNullOrWhiteSpace(appBaseDir))
            {
                candidates.Add(appBaseDir);
                try
                {
                    string parent = Path.GetDirectoryName(appBaseDir);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        candidates.Add(parent);
                    }
                }
                catch
                {
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < candidates.Count; i++)
            {
                string cand = candidates[i];
                if (string.IsNullOrWhiteSpace(cand) || !seen.Add(cand))
                {
                    continue;
                }
                if (IsValidGameDirectory(cand))
                {
                    return SafeFullPath(cand);
                }
            }
            return null;
        }

        public static string ResolveRequiredGameDirectory(string explicitGameDir, string steamDir, string appBaseDir)
        {
            if (!string.IsNullOrWhiteSpace(explicitGameDir))
            {
                string norm = NormalizePickedGameDirectory(explicitGameDir);
                if (!IsValidGameDirectory(norm))
                {
                    throw new DirectoryNotFoundException(
                        "В указанной папке не найдены файлы Zombies Aren't Real (zombies-arent-real.exe и lang\\en.lang): " + norm);
                }
                return norm;
            }

            string detected = AutoDetectGameDirectory(steamDir, appBaseDir);
            if (string.IsNullOrEmpty(detected))
            {
                throw new DirectoryNotFoundException(
                    "Не удалось автоматически найти папку игры Zombies Aren't Real. Укажите путь вручную через --game-dir или кнопку «Обзор...».");
            }
            return detected;
        }

        public static void AssertGameClosed(string resolvedGameDir)
        {
            string targetExe = Path.Combine(resolvedGameDir, GameExeName);
            Process[] procs;
            try
            {
                procs = Process.GetProcessesByName("zombies-arent-real");
            }
            catch
            {
                return;
            }

            for (int i = 0; i < procs.Length; i++)
            {
                Process p = procs[i];
                try
                {
                    string moduleFile = null;
                    try
                    {
                        if (p.MainModule != null)
                        {
                            moduleFile = p.MainModule.FileName;
                        }
                    }
                    catch
                    {
                        moduleFile = null;
                    }

                    if (moduleFile == null || string.Equals(moduleFile, targetExe, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            "Игра Zombies Aren't Real сейчас запущена (PID " + p.Id + "). Закройте игру перед установкой или возвратом оригинала.");
                    }
                }
                finally
                {
                    p.Dispose();
                }
            }
        }

        private static void AddRegistrySteamCandidates(List<string> list)
        {
            string[][] probes = new string[][]
            {
                new string[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath" },
                new string[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath" },
                new string[] { @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath" }
            };

            for (int i = 0; i < probes.Length; i++)
            {
                try
                {
                    string raw = Registry.GetValue(probes[i][0], probes[i][1], null) as string;
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        list.Add(Program.SafeTrimTrailingSlash(raw.Replace('/', '\\')));
                    }
                }
                catch
                {
                }
            }
        }

        private static void AddLibraryCandidates(List<string> candidates, string libraryRoot)
        {
            if (string.IsNullOrWhiteSpace(libraryRoot))
            {
                return;
            }
            try
            {
                string steamapps = Path.Combine(libraryRoot, "steamapps");
                string manifestPath = Path.Combine(steamapps, "appmanifest_5185890.acf");
                if (SafeFileExists(manifestPath))
                {
                    string acf = File.ReadAllText(manifestPath, Encoding.UTF8);
                    Match m = Regex.Match(acf, "\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.CultureInvariant);
                    if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
                    {
                        candidates.Add(Path.Combine(steamapps, Path.Combine("common", m.Groups[1].Value)));
                    }
                }
                candidates.Add(Path.Combine(steamapps, @"common\" + DefaultGameFolderName));
            }
            catch
            {
            }
        }

        private static void AddProtonSteamCandidates(List<string> list)
        {
            list.Add(@"Z:\home\deck\.local\share\Steam");
            list.Add(@"Z:\home\deck\.steam\steam");
            list.Add(@"Z:\home\deck\.steam\root");
            list.Add(@"Z:\home\deck\.var\app\com.valvesoftware.Steam\.local\share\Steam");

            try
            {
                if (SafeDirectoryExists(@"Z:\home"))
                {
                    string[] users = Directory.GetDirectories(@"Z:\home");
                    for (int i = 0; i < users.Length; i++)
                    {
                        list.Add(Path.Combine(users[i], @".local\share\Steam"));
                        list.Add(Path.Combine(users[i], @".steam\steam"));
                        list.Add(Path.Combine(users[i], @".var\app\com.valvesoftware.Steam\.local\share\Steam"));
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddProtonLibraryCandidates(List<string> candidates)
        {
            List<string> protonRoots = new List<string>();
            AddProtonSteamCandidates(protonRoots);
            for (int i = 0; i < protonRoots.Count; i++)
            {
                AddLibraryCandidates(candidates, protonRoots[i]);
                AddVdfLibraryCandidates(candidates, Path.Combine(protonRoots[i], @"steamapps\libraryfolders.vdf"));
            }

            string[] sdRoots = new string[]
            {
                @"Z:\run\media\mmcblk0p1",
                @"Z:\run\media\mmcblk0p1\SteamLibrary"
            };
            for (int i = 0; i < sdRoots.Length; i++)
            {
                AddLibraryCandidates(candidates, sdRoots[i]);
            }

            try
            {
                if (SafeDirectoryExists(@"Z:\run\media"))
                {
                    string[] mediaEntries = Directory.GetDirectories(@"Z:\run\media");
                    for (int i = 0; i < mediaEntries.Length; i++)
                    {
                        AddLibraryCandidates(candidates, mediaEntries[i]);
                        AddLibraryCandidates(candidates, Path.Combine(mediaEntries[i], "SteamLibrary"));
                        try
                        {
                            string[] subMounts = Directory.GetDirectories(mediaEntries[i]);
                            for (int j = 0; j < subMounts.Length; j++)
                            {
                                AddLibraryCandidates(candidates, subMounts[j]);
                                AddLibraryCandidates(candidates, Path.Combine(subMounts[j], "SteamLibrary"));
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddVdfLibraryCandidates(List<string> candidates, string vdfPath)
        {
            if (!SafeFileExists(vdfPath))
            {
                return;
            }
            try
            {
                string text = File.ReadAllText(vdfPath, Encoding.UTF8);
                MatchCollection matches = Regex.Matches(text, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.CultureInvariant);
                foreach (Match match in matches)
                {
                    string rawVal = match.Groups[1].Value;
                    string libRoot = Program.SafeTrimTrailingSlash(rawVal.Replace("\\\\", "\\"));
                    AddLibraryCandidates(candidates, libRoot);
                    if (rawVal.StartsWith("/", StringComparison.Ordinal))
                    {
                        string protonLib = @"Z:" + Program.SafeTrimTrailingSlash(rawVal.Replace('/', '\\'));
                        AddLibraryCandidates(candidates, protonLib);
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryAddSteamRootFromGameDir(List<string> candidates, string gameDir)
        {
            try
            {
                string clean = Program.SafeTrimTrailingSlash(gameDir.Trim().Trim('"'));
                DirectoryInfo commonDir = Directory.GetParent(clean);
                if (commonDir != null && string.Equals(commonDir.Name, "common", StringComparison.OrdinalIgnoreCase))
                {
                    DirectoryInfo steamappsDir = commonDir.Parent;
                    if (steamappsDir != null && string.Equals(steamappsDir.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        DirectoryInfo rootDir = steamappsDir.Parent;
                        if (rootDir != null)
                        {
                            candidates.Add(rootDir.FullName);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static List<string> GetSafeLocalDriveRoots()
        {
            List<string> roots = new List<string>();
            HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                DriveInfo[] drives = DriveInfo.GetDrives();
                for (int i = 0; i < drives.Length; i++)
                {
                    DriveInfo d = drives[i];
                    if ((d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady)
                    {
                        if (added.Add(d.Name))
                        {
                            roots.Add(d.Name);
                        }
                    }
                }
            }
            catch
            {
            }

            for (char c = 'C'; c <= 'Z'; c++)
            {
                string root = c + @":\";
                if (!added.Contains(root) && SafeDirectoryExists(root))
                {
                    added.Add(root);
                    roots.Add(root);
                }
            }

            return roots;
        }

        public static bool SafeFileExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public static bool SafeDirectoryExists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static string SafeFullPath(string path)
        {
            try
            {
                string full = Program.SafeTrimTrailingSlash(Path.GetFullPath(path));
                return NormalizeExactCasePath(full);
            }
            catch
            {
                return Program.SafeTrimTrailingSlash(path);
            }
        }

        private static string NormalizeExactCasePath(string fullPath)
        {
            try
            {
                if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
                {
                    return fullPath;
                }
                DirectoryInfo di = new DirectoryInfo(fullPath);
                List<string> parts = new List<string>();
                DirectoryInfo cur = di;
                while (cur.Parent != null)
                {
                    FileSystemInfo[] entries = cur.Parent.GetFileSystemInfos(cur.Name);
                    parts.Add(entries.Length > 0 ? entries[0].Name : cur.Name);
                    cur = cur.Parent;
                }
                parts.Reverse();
                string root = cur.FullName.ToUpperInvariant();
                for (int i = 0; i < parts.Count; i++)
                {
                    root = Path.Combine(root, parts[i]);
                }
                return Program.SafeTrimTrailingSlash(root);
            }
            catch
            {
                return fullPath;
            }
        }
    }

    internal struct LangPatchStats
    {
        public int TotalKeys;
        public int TranslatedKeys;
        public int FallbackRuKeys;
        public int FallbackEnKeys;
    }

    internal static class LangPatcher
    {
        private static readonly Regex PlaceholderRegex = new Regex(@"\{[^{}]+\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        public static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly string[] RuTopHeaderLines = new string[]
        {
            "# Zombies Aren't Real - Russian translation (lang/ru.lang) of lang/en.lang.",
            "#",
            "# Same keys and {placeholders} as lang/en.lang, in the same order; only the text between them",
            "# is translated. The notes on each section are lang/en.lang's, kept in English for whoever",
            "# edits this file. Every line is translated: test_lang_files_match fails on one left out. Written in UTF-8: the game",
            "# loads this file in its Cyrillic codepage (src/glyphs.odin) and draws it in a font file that",
            "# has Cyrillic letters (the bundled Carlito when the chosen font has none, src/fontfiles.odin)."
        };

        public static Dictionary<string, string> ParseKeyValueMap(string langContent)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(langContent))
            {
                return map;
            }

            string[] lines = SplitLinesPreserveEmpty(langContent);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
                {
                    continue;
                }
                int eq = lines[i].IndexOf('=');
                if (eq < 0)
                {
                    continue;
                }
                string key = lines[i].Substring(0, eq).Trim();
                string val = lines[i].Substring(eq + 1).Trim();
                if (key.Length > 0)
                {
                    map[key] = val;
                }
            }
            return map;
        }

        public static byte[] PatchLangToUtf8Crlf(
            string enLangContent,
            Dictionary<string, string> payloadLang,
            Dictionary<string, string> fallbackRuLang,
            out LangPatchStats stats)
        {
            stats = new LangPatchStats();
            bool endsWithNewline = enLangContent.EndsWith("\n", StringComparison.Ordinal);
            string[] rawLines = SplitLinesPreserveEmpty(enLangContent);
            int rawEffectiveLength = endsWithNewline && rawLines.Length > 0 && rawLines[rawLines.Length - 1].Length == 0
                ? rawLines.Length - 1
                : rawLines.Length;

            string[] lines = ReplaceTopHeaderBlock(rawLines, rawEffectiveLength);
            int effectiveLength = lines.Length;

            StringBuilder sb = new StringBuilder(enLangContent.Length + 65536);
            for (int i = 0; i < effectiveLength; i++)
            {
                string rawLine = lines[i];
                string trimmed = rawLine.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == ';')
                {
                    sb.Append(rawLine);
                }
                else
                {
                    int eq = rawLine.IndexOf('=');
                    if (eq < 0)
                    {
                        sb.Append(rawLine);
                    }
                    else
                    {
                        string key = rawLine.Substring(0, eq).Trim();
                        string enVal = rawLine.Substring(eq + 1).Trim();
                        stats.TotalKeys++;

                        string chosenVal = ResolveSafeTranslation(key, enVal, payloadLang, fallbackRuLang, ref stats);
                        sb.Append(key);
                        sb.Append(" = ");
                        sb.Append(chosenVal);
                    }
                }

                if (i < effectiveLength - 1 || endsWithNewline)
                {
                    sb.Append("\r\n");
                }
            }

            return Utf8NoBom.GetBytes(sb.ToString());
        }

        private static string ResolveSafeTranslation(
            string key,
            string enVal,
            Dictionary<string, string> payloadLang,
            Dictionary<string, string> fallbackRuLang,
            ref LangPatchStats stats)
        {
            string ruCandidate;
            if (payloadLang != null
                && payloadLang.TryGetValue(key, out ruCandidate)
                && IsValidReplacement(enVal, ruCandidate))
            {
                stats.TranslatedKeys++;
                return ruCandidate;
            }

            string origRuCandidate;
            if (fallbackRuLang != null
                && fallbackRuLang.TryGetValue(key, out origRuCandidate)
                && IsValidReplacement(enVal, origRuCandidate))
            {
                stats.FallbackRuKeys++;
                return origRuCandidate;
            }

            stats.FallbackEnKeys++;
            return enVal;
        }

        public static bool IsValidReplacement(string enVal, string ruVal)
        {
            if (ruVal == null)
            {
                return false;
            }
            if (enVal.Length > 0 && ruVal.Length == 0)
            {
                return false;
            }
            return TokensMultisetEqual(enVal, ruVal);
        }

        public static bool TokensMultisetEqual(string a, string b)
        {
            MatchCollection ma = PlaceholderRegex.Matches(a ?? string.Empty);
            MatchCollection mb = PlaceholderRegex.Matches(b ?? string.Empty);
            if (ma.Count != mb.Count)
            {
                return false;
            }
            if (ma.Count == 0)
            {
                return true;
            }
            string[] ta = new string[ma.Count];
            string[] tb = new string[mb.Count];
            for (int i = 0; i < ma.Count; i++)
            {
                ta[i] = ma[i].Value;
                tb[i] = mb[i].Value;
            }
            Array.Sort(ta, StringComparer.Ordinal);
            Array.Sort(tb, StringComparer.Ordinal);
            for (int i = 0; i < ta.Length; i++)
            {
                if (!string.Equals(ta[i], tb[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static string[] ReplaceTopHeaderBlock(string[] rawLines, int length)
        {
            int idx = 0;
            while (idx < length)
            {
                string trimmedLeft = rawLines[idx].TrimStart();
                if (trimmedLeft.StartsWith("#", StringComparison.Ordinal) || trimmedLeft.StartsWith(";", StringComparison.Ordinal))
                {
                    idx++;
                }
                else
                {
                    break;
                }
            }

            if (idx == 0)
            {
                string[] copy = new string[length];
                Array.Copy(rawLines, 0, copy, 0, length);
                return copy;
            }

            int restCount = length - idx;
            string[] result = new string[RuTopHeaderLines.Length + restCount];
            Array.Copy(RuTopHeaderLines, 0, result, 0, RuTopHeaderLines.Length);
            if (restCount > 0)
            {
                Array.Copy(rawLines, idx, result, RuTopHeaderLines.Length, restCount);
            }
            return result;
        }

        private static string[] SplitLinesPreserveEmpty(string content)
        {
            string normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalized.Split('\n');
        }
    }

    internal static class RawIniBytePatcher
    {
        public static byte[] UpdateSettingsLanguage(byte[] rawSettings, string targetLanguageLine, out string originalLanguageLine)
        {
            originalLanguageLine = null;
            byte[] replacementBytes = Encoding.ASCII.GetBytes(targetLanguageLine);

            using (MemoryStream ms = new MemoryStream(rawSettings.Length + 32))
            {
                int pos = 0;
                bool updated = false;

                while (pos < rawSettings.Length)
                {
                    int lineStart = pos;
                    int lineEnd;
                    int nextPos;
                    ReadRawLineBounds(rawSettings, pos, out lineEnd, out nextPos);

                    int contentStart = lineStart;
                    if (lineStart == 0
                        && lineEnd - lineStart >= 3
                        && rawSettings[0] == 0xEF
                        && rawSettings[1] == 0xBB
                        && rawSettings[2] == 0xBF)
                    {
                        contentStart = 3;
                    }

                    int trimStart;
                    int trimEnd;
                    TrimAsciiSpaces(rawSettings, contentStart, lineEnd, out trimStart, out trimEnd);

                    bool replacedThisLine = false;
                    if (!updated && trimEnd > trimStart)
                    {
                        byte first = rawSettings[trimStart];
                        if (first != (byte)'#' && first != (byte)';' && first != (byte)'[')
                        {
                            int eqIdx = FindByte(rawSettings, trimStart, trimEnd, (byte)'=');
                            if (eqIdx > trimStart)
                            {
                                int keyStart;
                                int keyEnd;
                                TrimAsciiSpaces(rawSettings, trimStart, eqIdx, out keyStart, out keyEnd);
                                if (AsciiSliceEquals(rawSettings, keyStart, keyEnd, "language"))
                                {
                                    originalLanguageLine = Encoding.UTF8.GetString(rawSettings, contentStart, lineEnd - contentStart);
                                    if (contentStart > lineStart)
                                    {
                                        ms.Write(rawSettings, lineStart, contentStart - lineStart);
                                    }
                                    ms.Write(replacementBytes, 0, replacementBytes.Length);
                                    ms.Write(rawSettings, lineEnd, nextPos - lineEnd);
                                    updated = true;
                                    replacedThisLine = true;
                                }
                            }
                        }
                    }

                    if (!replacedThisLine)
                    {
                        ms.Write(rawSettings, lineStart, nextPos - lineStart);
                    }
                    pos = nextPos;
                }

                return ms.ToArray();
            }
        }

        private static void ReadRawLineBounds(byte[] buf, int start, out int lineEnd, out int nextPos)
        {
            int idx = start;
            while (idx < buf.Length && buf[idx] != (byte)'\n')
            {
                idx++;
            }
            if (idx < buf.Length)
            {
                nextPos = idx + 1;
                lineEnd = (idx > start && buf[idx - 1] == (byte)'\r') ? idx - 1 : idx;
            }
            else
            {
                lineEnd = buf.Length;
                nextPos = buf.Length;
            }
        }

        private static void TrimAsciiSpaces(byte[] buf, int start, int end, out int trimStart, out int trimEnd)
        {
            trimStart = start;
            while (trimStart < end && (buf[trimStart] == (byte)' ' || buf[trimStart] == (byte)'\t'))
            {
                trimStart++;
            }
            trimEnd = end;
            while (trimEnd > trimStart && (buf[trimEnd - 1] == (byte)' ' || buf[trimEnd - 1] == (byte)'\t'))
            {
                trimEnd--;
            }
        }

        private static int FindByte(byte[] buf, int start, int end, byte needle)
        {
            for (int i = start; i < end; i++)
            {
                if (buf[i] == needle)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool AsciiSliceEquals(byte[] buf, int start, int end, string expected)
        {
            if (end - start != expected.Length)
            {
                return false;
            }
            for (int i = 0; i < expected.Length; i++)
            {
                if (buf[start + i] != (byte)expected[i])
                {
                    return false;
                }
            }
            return true;
        }
    }

    internal sealed class BackupFileRecord
    {
        public string TargetKind;
        public string RelPath;
        public string BackupRelPath;
        public string OrigSha256;
        public string PatchedSha256;
        public bool ExistedOriginally;
    }

    internal sealed class BackupManifestData
    {
        public int ManifestVersion = 1;
        public string InstallerVersion = "1.1.0";
        public int AppId = Program.TargetAppId;
        public string CreatedUtc;
        public string UpdatedUtc;
        public bool SettingsExisted;
        public string OriginalLanguageLine;
        public string SteamSchemaSourcePath;
        public List<BackupFileRecord> Files = new List<BackupFileRecord>();
    }

    internal static class BackupManifestStore
    {
        public const string BackupFolderName = "ru_backup_orig";
        public const string ManifestFileName = "backup_manifest.json";

        public static string GetBackupDirectory(string gameDir)
        {
            return Path.Combine(gameDir, BackupFolderName);
        }

        public static string GetManifestPath(string gameDir)
        {
            return Path.Combine(GetBackupDirectory(gameDir), ManifestFileName);
        }

        public static bool HasValidBackup(string gameDir)
        {
            if (!SteamLocator.IsValidGameDirectory(gameDir))
            {
                return false;
            }
            string manifestPath = GetManifestPath(gameDir);
            if (!SteamLocator.SafeFileExists(manifestPath))
            {
                return false;
            }
            try
            {
                BackupManifestData m = Load(manifestPath);
                return m != null && m.Files != null && m.Files.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        public static BackupManifestData Load(string manifestPath)
        {
            string json = File.ReadAllText(manifestPath, Encoding.UTF8);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            IDictionary<string, object> root = serializer.DeserializeObject(json) as IDictionary<string, object>;
            if (root == null)
            {
                throw new InvalidDataException("Повреждён файл backup_manifest.json.");
            }

            BackupManifestData m = new BackupManifestData();
            m.ManifestVersion = ReadInt(root, "manifest_version", 1);
            m.InstallerVersion = ReadString(root, "installer_version", "1.1.0");
            m.AppId = ReadInt(root, "app_id", Program.TargetAppId);
            m.CreatedUtc = ReadString(root, "created_utc", string.Empty);
            m.UpdatedUtc = ReadString(root, "updated_utc", string.Empty);
            m.SettingsExisted = ReadBool(root, "settings_existed", false);
            m.OriginalLanguageLine = ReadString(root, "original_language_line", null);
            m.SteamSchemaSourcePath = ReadString(root, "steam_schema_source_path", null);

            object filesObj;
            if (root.TryGetValue("files", out filesObj) && filesObj is IEnumerable)
            {
                foreach (object item in (IEnumerable)filesObj)
                {
                    IDictionary<string, object> d = item as IDictionary<string, object>;
                    if (d == null)
                    {
                        continue;
                    }
                    BackupFileRecord rec = new BackupFileRecord();
                    rec.TargetKind = ReadString(d, "target_kind", "game");
                    rec.RelPath = ReadString(d, "rel_path", string.Empty).Replace('/', '\\');
                    rec.BackupRelPath = ReadString(d, "backup_rel_path", string.Empty).Replace('/', '\\');
                    rec.OrigSha256 = ReadString(d, "orig_sha256", string.Empty);
                    rec.PatchedSha256 = ReadString(d, "patched_sha256", string.Empty);
                    rec.ExistedOriginally = ReadBool(d, "existed_originally", true);
                    m.Files.Add(rec);
                }
            }
            return m;
        }

        public static void Save(string manifestPath, BackupManifestData m)
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["manifest_version"] = m.ManifestVersion;
            root["installer_version"] = m.InstallerVersion;
            root["app_id"] = m.AppId;
            root["created_utc"] = m.CreatedUtc ?? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            root["updated_utc"] = m.UpdatedUtc ?? DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            root["settings_existed"] = m.SettingsExisted;
            root["original_language_line"] = m.OriginalLanguageLine;
            root["steam_schema_source_path"] = m.SteamSchemaSourcePath;

            List<Dictionary<string, object>> filesList = new List<Dictionary<string, object>>(m.Files.Count);
            for (int i = 0; i < m.Files.Count; i++)
            {
                BackupFileRecord r = m.Files[i];
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["target_kind"] = r.TargetKind;
                d["rel_path"] = r.RelPath;
                d["backup_rel_path"] = r.BackupRelPath;
                d["orig_sha256"] = r.OrigSha256;
                d["patched_sha256"] = r.PatchedSha256;
                d["existed_originally"] = r.ExistedOriginally;
                filesList.Add(d);
            }
            root["files"] = filesList;

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            string rawJson = serializer.Serialize(root);
            string formatted = FormatCompactJson(rawJson);

            string dir = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllBytes(manifestPath, LangPatcher.Utf8NoBom.GetBytes(formatted));
        }

        public static string ComputeSha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        private static string FormatCompactJson(string json)
        {
            StringBuilder sb = new StringBuilder(json.Length + 256);
            int indent = 0;
            bool inQuotes = false;
            bool escaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    sb.Append(c);
                    escaped = false;
                    continue;
                }
                if (c == '\\' && inQuotes)
                {
                    sb.Append(c);
                    escaped = true;
                    continue;
                }
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                    continue;
                }
                if (inQuotes)
                {
                    sb.Append(c);
                    continue;
                }

                if (c == '{' || c == '[')
                {
                    sb.Append(c);
                    sb.Append("\r\n");
                    indent++;
                    AppendIndent(sb, indent);
                }
                else if (c == '}' || c == ']')
                {
                    sb.Append("\r\n");
                    indent = Math.Max(0, indent - 1);
                    AppendIndent(sb, indent);
                    sb.Append(c);
                }
                else if (c == ',')
                {
                    sb.Append(",\r\n");
                    AppendIndent(sb, indent);
                }
                else if (c == ':')
                {
                    sb.Append(": ");
                }
                else
                {
                    sb.Append(c);
                }
            }
            sb.Append("\r\n");
            return sb.ToString();
        }

        private static void AppendIndent(StringBuilder sb, int level)
        {
            for (int i = 0; i < level; i++)
            {
                sb.Append("  ");
            }
        }

        private static string ReadString(IDictionary<string, object> d, string key, string fallback)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
            return fallback;
        }

        private static int ReadInt(IDictionary<string, object> d, string key, int fallback)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            return fallback;
        }

        private static bool ReadBool(IDictionary<string, object> d, string key, bool fallback)
        {
            object v;
            if (d.TryGetValue(key, out v) && v != null)
            {
                return Convert.ToBoolean(v, CultureInfo.InvariantCulture);
            }
            return fallback;
        }
    }

    internal static class ElevationHelper
    {
        public static bool IsProcessElevated()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    if (id == null)
                    {
                        return false;
                    }
                    WindowsPrincipal principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool NeedsElevationForGameDir(string gameDir)
        {
            if (string.IsNullOrWhiteSpace(gameDir) || IsProcessElevated())
            {
                return false;
            }
            if (!CanProbeDirectoryWrite(gameDir))
            {
                return true;
            }
            string fontsDir = Path.Combine(gameDir, "fonts");
            if (SteamLocator.SafeDirectoryExists(fontsDir) && !CanProbeDirectoryWrite(fontsDir))
            {
                return true;
            }
            string ruLangPath = Path.Combine(gameDir, @"lang\ru.lang");
            if (SteamLocator.SafeFileExists(ruLangPath) && !CanOpenExistingFileForWrite(ruLangPath))
            {
                return true;
            }
            return false;
        }

        public static bool NeedsElevationForTargetFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || IsProcessElevated())
            {
                return false;
            }
            string parentDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parentDir) && SteamLocator.SafeDirectoryExists(parentDir))
            {
                if (!CanProbeDirectoryWrite(parentDir))
                {
                    return true;
                }
            }
            if (SteamLocator.SafeFileExists(filePath) && !CanOpenExistingFileForWrite(filePath))
            {
                return true;
            }
            return false;
        }

        public static string BuildUacArguments(string modeFlag, string gameDir, string steamDir)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(modeFlag);
            if (!string.IsNullOrWhiteSpace(gameDir))
            {
                sb.Append(" --game-dir ");
                sb.Append(QuoteCliValue(gameDir));
            }
            if (!string.IsNullOrWhiteSpace(steamDir))
            {
                sb.Append(" --steam-dir ");
                sb.Append(QuoteCliValue(steamDir));
            }
            return sb.ToString();
        }

        public static bool TryRelaunchElevated(
            string modeFlag,
            string gameDir,
            string steamDir,
            out bool cancelledByUser,
            out string errorMessage)
        {
            cancelledByUser = false;
            errorMessage = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = BuildUacArguments(modeFlag, gameDir, steamDir);
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                Process proc = Process.Start(psi);
                return proc != null;
            }
            catch (Win32Exception wex)
            {
                if (wex.NativeErrorCode == 1223)
                {
                    cancelledByUser = true;
                    return false;
                }
                errorMessage = wex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string QuoteCliValue(string raw)
        {
            string clean = Program.SafeTrimTrailingSlash((raw ?? string.Empty).Trim().Trim('"'));
            if (clean.EndsWith(@"\", StringComparison.Ordinal))
            {
                clean += @"\";
            }
            return "\"" + clean.Replace("\"", "\\\"") + "\"";
        }

        private static bool CanProbeDirectoryWrite(string dirPath)
        {
            string probePath = Path.Combine(dirPath, ".zar_uac_probe_" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream fs = new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch
            {
                return true;
            }
            finally
            {
                try
                {
                    if (File.Exists(probePath))
                    {
                        File.Delete(probePath);
                    }
                }
                catch
                {
                }
            }
        }

        private static bool CanOpenExistingFileForWrite(string filePath)
        {
            try
            {
                FileAttributes attrs = File.GetAttributes(filePath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    return true;
                }
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch
            {
                return true;
            }
        }
    }

    internal static class InstallerService
    {
        private static readonly string[] ManagedOriginalFonts = new string[]
        {
            @"fonts\Carlito.ttf",
            @"fonts\Carlito-OFL.txt"
        };

        private static readonly string[] AddedOptionalFonts = new string[]
        {
            @"fonts\Tactical_HUD.ttf",
            @"fonts\Tactical_HUD-OFL.txt"
        };

        public static int RunStatusCli(PayloadData payload, string appBaseDir, string explicitGameDir, string explicitSteamDir)
        {
            string steamDir = SteamLocator.DetectSteamDirectory(explicitSteamDir, explicitGameDir);
            string gameDir = !string.IsNullOrWhiteSpace(explicitGameDir)
                ? SteamLocator.NormalizePickedGameDirectory(explicitGameDir)
                : SteamLocator.AutoDetectGameDirectory(steamDir, appBaseDir);

            bool validGame = SteamLocator.IsValidGameDirectory(gameDir);
            if (validGame && string.IsNullOrEmpty(steamDir))
            {
                steamDir = SteamLocator.DetectSteamDirectory(null, gameDir);
            }

            bool installed = validGame && IsRussifierInstalled(gameDir);
            bool hasManifest = validGame && BackupManifestStore.HasValidBackup(gameDir);

            Console.WriteLine("GameDir: " + (validGame ? gameDir : "NOT_FOUND"));
            Console.WriteLine("SteamDir: " + (!string.IsNullOrEmpty(steamDir) ? steamDir : "NOT_FOUND"));
            Console.WriteLine("Installed: " + (installed ? "true" : "false"));
            Console.WriteLine("BackupManifest: " + (hasManifest ? "present" : "absent"));

            if (validGame)
            {
                List<string> taintedCampaigns = CheckCampaignAsciiCleanliness(gameDir);
                Console.WriteLine("CampaignFilesClean: " + (taintedCampaigns.Count == 0 ? "true" : "warning (" + string.Join(", ", taintedCampaigns.ToArray()) + ")"));
                List<string> taintedMutators = CheckMutatorAsciiCleanliness(gameDir);
                Console.WriteLine("MutatorFilesClean: " + (taintedMutators.Count == 0 ? "true" : "warning (" + string.Join(", ", taintedMutators.ToArray()) + ")"));
            }

            return validGame ? 0 : 2;
        }

        public static bool IsRussifierInstalled(string gameDir)
        {
            if (!BackupManifestStore.HasValidBackup(gameDir))
            {
                return false;
            }
            try
            {
                BackupManifestData manifest = BackupManifestStore.Load(BackupManifestStore.GetManifestPath(gameDir));
                for (int i = 0; i < manifest.Files.Count; i++)
                {
                    BackupFileRecord rec = manifest.Files[i];
                    if (string.Equals(rec.RelPath, @"lang\ru.lang", StringComparison.OrdinalIgnoreCase))
                    {
                        string liveRu = Path.Combine(gameDir, @"lang\ru.lang");
                        if (!SteamLocator.SafeFileExists(liveRu))
                        {
                            return false;
                        }
                        string liveHash = BackupManifestStore.ComputeSha256Hex(File.ReadAllBytes(liveRu));
                        return string.Equals(liveHash, rec.PatchedSha256, StringComparison.OrdinalIgnoreCase);
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Install(PayloadData payload, string gameDir, string steamDir, Action<string> log)
        {
            string ignoredSkippedSchema;
            Install(payload, gameDir, steamDir, log, out ignoredSkippedSchema);
        }

        public static void Install(PayloadData payload, string gameDir, string steamDir, Action<string> log, out string skippedSchemaPath)
        {
            skippedSchemaPath = null;
            if (log == null)
            {
                log = delegate { };
            }
            if (!SteamLocator.IsValidGameDirectory(gameDir))
            {
                throw new DirectoryNotFoundException("Папка игры не содержит zombies-arent-real.exe и lang\\en.lang: " + gameDir);
            }

            SteamLocator.AssertGameClosed(gameDir);

            log("Проверка папки игры: " + gameDir);
            List<string> nonAsciiCmp = CheckCampaignAsciiCleanliness(gameDir);
            if (nonAsciiCmp.Count > 0)
            {
                log("Внимание: обнаружены не-ASCII байты в файлах кампаний (" + string.Join(", ", nonAsciiCmp.ToArray()) + "). Кампании не изменяются установщиком; при проблемах с сохранениями проверьте целостность файлов в Steam.");
            }
            List<string> nonAsciiMut = CheckMutatorAsciiCleanliness(gameDir);
            if (nonAsciiMut.Count > 0)
            {
                log("Внимание: обнаружены не-ASCII байты в файлах мутаторов (" + string.Join(", ", nonAsciiMut.ToArray()) + "). Проверьте целостность файлов в Steam.");
            }

            string backupRoot = BackupManifestStore.GetBackupDirectory(gameDir);
            string manifestPath = BackupManifestStore.GetManifestPath(gameDir);
            string stagingRoot = Path.Combine(backupRoot, "_staging");

            BackupManifestData existingManifest = null;
            if (SteamLocator.SafeFileExists(manifestPath))
            {
                try
                {
                    existingManifest = BackupManifestStore.Load(manifestPath);
                }
                catch
                {
                    existingManifest = null;
                }
            }

            BackupManifestData nextManifest = new BackupManifestData();
            nextManifest.InstallerVersion = payload.Version ?? "1.1.0";
            nextManifest.AppId = payload.AppId;
            nextManifest.CreatedUtc = (existingManifest != null && !string.IsNullOrEmpty(existingManifest.CreatedUtc))
                ? existingManifest.CreatedUtc
                : DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            nextManifest.UpdatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            EnsureEmptyDirectory(stagingRoot);

            try
            {
                // 1. Back up & patch lang\ru.lang
                BackupFileRecord langRec = EnsureGameFileBackedUp(gameDir, backupRoot, existingManifest, @"lang\ru.lang", log);
                string enLangText = File.ReadAllText(Path.Combine(gameDir, @"lang\en.lang"), Encoding.UTF8);
                Dictionary<string, string> fallbackRuMap = new Dictionary<string, string>(StringComparer.Ordinal);
                string backupRuLangPath = Path.Combine(backupRoot, langRec.BackupRelPath);
                if (SteamLocator.SafeFileExists(backupRuLangPath))
                {
                    fallbackRuMap = LangPatcher.ParseKeyValueMap(File.ReadAllText(backupRuLangPath, Encoding.UTF8));
                }

                LangPatchStats langStats;
                byte[] patchedRuLang = LangPatcher.PatchLangToUtf8Crlf(enLangText, payload.Lang, fallbackRuMap, out langStats);
                WriteViaStaging(stagingRoot, @"lang\ru.lang", Path.Combine(gameDir, @"lang\ru.lang"), patchedRuLang);
                langRec.PatchedSha256 = BackupManifestStore.ComputeSha256Hex(patchedRuLang);
                nextManifest.Files.Add(langRec);

                if (langStats.FallbackRuKeys > 0 || langStats.FallbackEnKeys > 0)
                {
                    log(string.Format(
                        CultureInfo.InvariantCulture,
                        "Обновлён lang\\ru.lang: переведено {0} из {1} ключей (резерв совместимости обновления: {2}).",
                        langStats.TranslatedKeys,
                        langStats.TotalKeys,
                        langStats.FallbackRuKeys + langStats.FallbackEnKeys));
                }
                else
                {
                    log(string.Format(
                        CultureInfo.InvariantCulture,
                        "Обновлён lang\\ru.lang: все ключи ({0}) переведены и проверены.",
                        langStats.TranslatedKeys));
                }

                // 2. Back up original fonts & install pixelated Carlito + Tactical HUD
                for (int i = 0; i < ManagedOriginalFonts.Length; i++)
                {
                    string relFont = ManagedOriginalFonts[i];
                    BackupFileRecord fontRec = EnsureGameFileBackedUp(gameDir, backupRoot, existingManifest, relFont, log);
                    string fontFileName = Path.GetFileName(relFont);
                    byte[] fontBytes;
                    if (!payload.Fonts.TryGetValue(fontFileName, out fontBytes))
                    {
                        throw new InvalidDataException("В ресурсе русификатора отсутствует шрифт " + fontFileName);
                    }
                    WriteViaStaging(stagingRoot, relFont, Path.Combine(gameDir, relFont), fontBytes);
                    fontRec.PatchedSha256 = BackupManifestStore.ComputeSha256Hex(fontBytes);
                    nextManifest.Files.Add(fontRec);
                }

                for (int i = 0; i < AddedOptionalFonts.Length; i++)
                {
                    string relOptFont = AddedOptionalFonts[i];
                    string optFileName = Path.GetFileName(relOptFont);
                    byte[] optBytes;
                    if (payload.Fonts.TryGetValue(optFileName, out optBytes))
                    {
                        WriteViaStaging(stagingRoot, relOptFont, Path.Combine(gameDir, relOptFont), optBytes);
                    }
                }
                log("Установлены кириллические шрифты: пиксельный 8x8 и гладкий Tactical HUD.");

                // 4. Back up & update steam_cloud\settings.ini (language = ru) if present
                string settingsRel = @"steam_cloud\settings.ini";
                string settingsLivePath = Path.Combine(gameDir, settingsRel);
                if (SteamLocator.SafeFileExists(settingsLivePath))
                {
                    byte[] liveSettingsBytes = File.ReadAllBytes(settingsLivePath);
                    string backupSettingsPath = Path.Combine(backupRoot, settingsRel);
                    if (existingManifest == null || !SteamLocator.SafeFileExists(backupSettingsPath))
                    {
                        EnsureParentDirectory(backupSettingsPath);
                        File.WriteAllBytes(backupSettingsPath, liveSettingsBytes);
                    }

                    string detectedOrigLangLine;
                    byte[] updatedSettings = RawIniBytePatcher.UpdateSettingsLanguage(liveSettingsBytes, "language = ru", out detectedOrigLangLine);

                    nextManifest.SettingsExisted = true;
                    if (existingManifest != null && existingManifest.SettingsExisted && existingManifest.OriginalLanguageLine != null)
                    {
                        nextManifest.OriginalLanguageLine = existingManifest.OriginalLanguageLine;
                    }
                    else
                    {
                        nextManifest.OriginalLanguageLine = detectedOrigLangLine;
                    }

                    BackupFileRecord settingsRec = new BackupFileRecord();
                    settingsRec.TargetKind = "settings_reference";
                    settingsRec.RelPath = settingsRel;
                    settingsRec.BackupRelPath = settingsRel;
                    settingsRec.ExistedOriginally = true;
                    settingsRec.OrigSha256 = BackupManifestStore.ComputeSha256Hex(File.ReadAllBytes(backupSettingsPath));
                    settingsRec.PatchedSha256 = BackupManifestStore.ComputeSha256Hex(updatedSettings);
                    nextManifest.Files.Add(settingsRec);

                    WriteViaStaging(stagingRoot, settingsRel, settingsLivePath, updatedSettings);
                    log("В настройках steam_cloud\\settings.ini включён русский язык (остальные параметры сохранены).");
                }
                else
                {
                    nextManifest.SettingsExisted = false;
                    nextManifest.OriginalLanguageLine = null;
                }

                BackupFileRecord legacy = FindRecord(existingManifest, "steam_schema", SteamLocator.SchemaFileName);
                if (legacy != null)
                {
                    nextManifest.SteamSchemaSourcePath = existingManifest.SteamSchemaSourcePath;
                    nextManifest.Files.Add(legacy);
                }
                BackupManifestStore.Save(manifestPath, nextManifest);
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }

            AssertNoLeftoverFilesInEnumeratedDirs(gameDir);
            log("Готово: Русификатор установлен.");
        }

        public static void Uninstall(string gameDir, string steamDir, Action<string> log)
        {
            string ignoredSkippedSchema;
            Uninstall(gameDir, steamDir, log, out ignoredSkippedSchema);
        }

        public static void Uninstall(string gameDir, string steamDir, Action<string> log, out string skippedSchemaPath)
        {
            skippedSchemaPath = null;
            if (log == null)
            {
                log = delegate { };
            }
            if (!SteamLocator.IsValidGameDirectory(gameDir))
            {
                throw new DirectoryNotFoundException("Папка игры не содержит zombies-arent-real.exe и lang\\en.lang: " + gameDir);
            }

            SteamLocator.AssertGameClosed(gameDir);

            string backupRoot = BackupManifestStore.GetBackupDirectory(gameDir);
            string manifestPath = BackupManifestStore.GetManifestPath(gameDir);
            if (!SteamLocator.SafeFileExists(manifestPath))
            {
                throw new InvalidOperationException("Резервная копия ru_backup_orig не найдена: оригинальные файлы уже используются.");
            }

            BackupManifestData manifest = BackupManifestStore.Load(manifestPath);
            string stagingRoot = Path.Combine(backupRoot, "_staging");
            EnsureEmptyDirectory(stagingRoot);

            try
            {
                for (int i = 0; i < manifest.Files.Count; i++)
                {
                    BackupFileRecord rec = manifest.Files[i];
                    if (string.Equals(rec.TargetKind, "settings_reference", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(rec.TargetKind, "game", StringComparison.OrdinalIgnoreCase))
                    {
                        string targetPath = Path.Combine(gameDir, rec.RelPath);
                        if (!rec.ExistedOriginally)
                        {
                            TryDeleteFile(targetPath);
                            continue;
                        }

                        string backupFilePath = Path.Combine(backupRoot, rec.BackupRelPath);
                        if (!SteamLocator.SafeFileExists(backupFilePath))
                        {
                            throw new FileNotFoundException("Отсутствует файл резервной копии: " + backupFilePath);
                        }
                        byte[] origBytes = File.ReadAllBytes(backupFilePath);
                        VerifyExpectedHash(rec.RelPath, origBytes, rec.OrigSha256);
                        WriteViaStaging(stagingRoot, rec.RelPath, targetPath, origBytes);
                        VerifyExpectedHash(rec.RelPath, File.ReadAllBytes(targetPath), rec.OrigSha256);
                    }
                    else if (string.Equals(rec.TargetKind, "steam_schema", StringComparison.OrdinalIgnoreCase))
                    {
                        string statsDir = SteamLocator.ResolveSteamStatsDirectory(steamDir);
                        string schemaTarget = !string.IsNullOrEmpty(statsDir)
                            ? Path.Combine(statsDir, SteamLocator.SchemaFileName)
                            : manifest.SteamSchemaSourcePath;

                        if (!string.IsNullOrEmpty(schemaTarget))
                        {
                            string backupSchemaPath = Path.Combine(backupRoot, rec.BackupRelPath);
                            if (SteamLocator.SafeFileExists(backupSchemaPath))
                            {
                                byte[] origSchema = File.ReadAllBytes(backupSchemaPath);
                                VerifyExpectedHash(SteamLocator.SchemaFileName, origSchema, rec.OrigSha256);
                                try
                                {
                                    WriteAtomicTarget(stagingRoot, rec.BackupRelPath, schemaTarget, origSchema);
                                    VerifyExpectedHash(SteamLocator.SchemaFileName, File.ReadAllBytes(schemaTarget), rec.OrigSha256);
                                    log("Восстановлена оригинальная схема достижений Steam.");
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    skippedSchemaPath = schemaTarget;
                                    log("Примечание: нет прав записи в папку Steam (" + schemaTarget + "). Оригинальные файлы самой игры полностью восстановлены.");
                                }
                            }
                        }
                    }
                }

                // Restore original language = ... line in steam_cloud\settings.ini
                string settingsLivePath = Path.Combine(gameDir, @"steam_cloud\settings.ini");
                if (manifest.SettingsExisted
                    && !string.IsNullOrEmpty(manifest.OriginalLanguageLine)
                    && SteamLocator.SafeFileExists(settingsLivePath))
                {
                    byte[] currentSettings = File.ReadAllBytes(settingsLivePath);
                    string ignoredPrev;
                    byte[] restoredSettings = RawIniBytePatcher.UpdateSettingsLanguage(currentSettings, manifest.OriginalLanguageLine, out ignoredPrev);
                    WriteViaStaging(stagingRoot, @"steam_cloud\settings.ini", settingsLivePath, restoredSettings);
                    log("Восстановлен исходный параметр языка в steam_cloud\\settings.ini.");
                }

                for (int i = 0; i < AddedOptionalFonts.Length; i++)
                {
                    TryDeleteFile(Path.Combine(gameDir, AddedOptionalFonts[i]));
                }
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }

            if (!string.IsNullOrEmpty(skippedSchemaPath))
            {
                log("Сохранена резервная копия предыдущей версии: " + backupRoot);
                return;
            }
            TryDeleteDirectory(backupRoot);
            if (SteamLocator.SafeDirectoryExists(backupRoot))
            {
                throw new IOException("Не удалось полностью удалить временную папку резервной копии: " + backupRoot);
            }

            AssertNoLeftoverFilesInEnumeratedDirs(gameDir);
            log("Готово: Оригинальные файлы игры полностью восстановлены.");
        }

        public static List<string> CheckCampaignAsciiCleanliness(string gameDir)
        {
            return CheckSubdirAsciiCleanliness(gameDir, "campaigns", "*.cmp");
        }

        public static List<string> CheckMutatorAsciiCleanliness(string gameDir)
        {
            return CheckSubdirAsciiCleanliness(gameDir, "mutators", "*.ini");
        }

        private static List<string> CheckSubdirAsciiCleanliness(string gameDir, string subFolder, string searchPattern)
        {
            List<string> tainted = new List<string>();
            string targetDir = Path.Combine(gameDir, subFolder);
            if (!SteamLocator.SafeDirectoryExists(targetDir))
            {
                return tainted;
            }
            try
            {
                string[] files = Directory.GetFiles(targetDir, searchPattern, SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    byte[] raw = File.ReadAllBytes(files[i]);
                    for (int b = 0; b < raw.Length; b++)
                    {
                        if (raw[b] >= 0x80)
                        {
                            tainted.Add(Path.GetFileName(files[i]));
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
            return tainted;
        }

        private static BackupFileRecord EnsureGameFileBackedUp(
            string gameDir,
            string backupRoot,
            BackupManifestData existingManifest,
            string relPath,
            Action<string> log)
        {
            string livePath = Path.Combine(gameDir, relPath);
            string backupPath = Path.Combine(backupRoot, relPath);
            BackupFileRecord prev = FindRecord(existingManifest, "game", relPath);

            if (!SteamLocator.SafeFileExists(livePath))
            {
                BackupFileRecord missingRec = new BackupFileRecord();
                missingRec.TargetKind = "game";
                missingRec.RelPath = relPath;
                missingRec.BackupRelPath = relPath;
                missingRec.ExistedOriginally = false;
                missingRec.OrigSha256 = string.Empty;
                return missingRec;
            }

            byte[] liveBytes = File.ReadAllBytes(livePath);
            string liveSha = BackupManifestStore.ComputeSha256Hex(liveBytes);

            bool reuseExistingBackup = false;
            if (prev != null && prev.ExistedOriginally && SteamLocator.SafeFileExists(backupPath))
            {
                byte[] backupBytes = File.ReadAllBytes(backupPath);
                string backupSha = BackupManifestStore.ComputeSha256Hex(backupBytes);
                if (string.Equals(backupSha, prev.OrigSha256, StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(liveSha, prev.PatchedSha256, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(liveSha, prev.OrigSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        reuseExistingBackup = true;
                    }
                    else
                    {
                        log("Обнаружено обновление игры в Steam для " + relPath + " — обновляется резервная копия.");
                    }
                }
            }

            if (!reuseExistingBackup)
            {
                EnsureParentDirectory(backupPath);
                File.WriteAllBytes(backupPath, liveBytes);
            }

            byte[] finalOrigBytes = File.ReadAllBytes(backupPath);
            BackupFileRecord rec = new BackupFileRecord();
            rec.TargetKind = "game";
            rec.RelPath = relPath;
            rec.BackupRelPath = relPath;
            rec.ExistedOriginally = true;
            rec.OrigSha256 = BackupManifestStore.ComputeSha256Hex(finalOrigBytes);
            return rec;
        }

        private static BackupFileRecord FindRecord(BackupManifestData m, string kind, string relPath)
        {
            if (m == null || m.Files == null)
            {
                return null;
            }
            for (int i = 0; i < m.Files.Count; i++)
            {
                BackupFileRecord r = m.Files[i];
                if (string.Equals(r.TargetKind, kind, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(r.RelPath, relPath, StringComparison.OrdinalIgnoreCase))
                {
                    return r;
                }
            }
            return null;
        }

        private static void WriteViaStaging(string stagingRoot, string relName, string targetPath, byte[] content)
        {
            string stageFile = Path.Combine(stagingRoot, relName);
            EnsureParentDirectory(stageFile);
            File.WriteAllBytes(stageFile, content);

            EnsureParentDirectory(targetPath);
            if (SteamLocator.SafeFileExists(targetPath))
            {
                FileAttributes attrs = File.GetAttributes(targetPath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(targetPath, attrs & ~FileAttributes.ReadOnly);
                }
            }
            File.Copy(stageFile, targetPath, true);
        }

        private static void WriteAtomicTarget(string stagingRoot, string relName, string targetPath, byte[] content)
        {
            string stageFile = Path.Combine(stagingRoot, relName);
            EnsureParentDirectory(stageFile);
            File.WriteAllBytes(stageFile, content);

            EnsureParentDirectory(targetPath);
            string sameVolumeTmp = targetPath + ".zar_tmp";
            try
            {
                File.WriteAllBytes(sameVolumeTmp, content);
                if (SteamLocator.SafeFileExists(targetPath))
                {
                    try
                    {
                        File.Replace(sameVolumeTmp, targetPath, null, true);
                        return;
                    }
                    catch
                    {
                    }
                }
                File.Copy(sameVolumeTmp, targetPath, true);
            }
            finally
            {
                TryDeleteFile(sameVolumeTmp);
            }
        }

        private static void VerifyExpectedHash(string label, byte[] actualBytes, string expectedHash)
        {
            if (string.IsNullOrEmpty(expectedHash))
            {
                return;
            }
            string actualHash = BackupManifestStore.ComputeSha256Hex(actualBytes);
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Несовпадение контрольной суммы SHA-256 при проверке " + label);
            }
        }

        private static void AssertNoLeftoverFilesInEnumeratedDirs(string gameDir)
        {
            string[] dirs = new string[] { "lang", "campaigns", "mutators" };
            for (int i = 0; i < dirs.Length; i++)
            {
                string sub = Path.Combine(gameDir, dirs[i]);
                if (!SteamLocator.SafeDirectoryExists(sub))
                {
                    continue;
                }
                string[] files = Directory.GetFiles(sub, "*", SearchOption.AllDirectories);
                for (int f = 0; f < files.Length; f++)
                {
                    string ext = Path.GetExtension(files[f]);
                    if (string.Equals(ext, ".bak", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext, ".tmp", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext, ".orig", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Обнаружен недопустимый временный файл в каталоге движка: " + files[f]);
                    }
                }
            }
        }

        private static void EnsureParentDirectory(string filePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private static void EnsureEmptyDirectory(string dirPath)
        {
            TryDeleteDirectory(dirPath);
            Directory.CreateDirectory(dirPath);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (SteamLocator.SafeFileExists(path))
                {
                    FileAttributes attrs = File.GetAttributes(path);
                    if ((attrs & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                    }
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteDirectory(string dirPath)
        {
            try
            {
                if (SteamLocator.SafeDirectoryExists(dirPath))
                {
                    string[] allFiles = Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories);
                    for (int i = 0; i < allFiles.Length; i++)
                    {
                        FileAttributes attrs = File.GetAttributes(allFiles[i]);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                        {
                            File.SetAttributes(allFiles[i], attrs & ~FileAttributes.ReadOnly);
                        }
                    }
                    Directory.Delete(dirPath, true);
                }
            }
            catch
            {
            }
        }
    }

    internal static class TacticalTheme
    {
        public static readonly Color BgDeep = Color.FromArgb(0x0F, 0x14, 0x19);
        public static readonly Color CardSurface = Color.FromArgb(0x18, 0x21, 0x2A);
        public static readonly Color CardLocked = Color.FromArgb(0x13, 0x1B, 0x22);
        public static readonly Color RecessedBg = Color.FromArgb(0x10, 0x16, 0x1D);
        public static readonly Color BorderCrisp = Color.FromArgb(0x2A, 0x38, 0x46);
        public static readonly Color BorderLocked = Color.FromArgb(0x22, 0x2E, 0x3A);
        public static readonly Color BorderEmerald = Color.FromArgb(0x26, 0x5A, 0x43);

        public static readonly Color EmeraldPrimary = Color.FromArgb(0x2E, 0xCC, 0x71);
        public static readonly Color EmeraldHover = Color.FromArgb(0x3B, 0xD9, 0x7E);
        public static readonly Color EmeraldPressed = Color.FromArgb(0x24, 0xB2, 0x60);
        public static readonly Color EmeraldDarkText = Color.FromArgb(0x08, 0x15, 0x0E);
        public static readonly Color EmeraldPillBg = Color.FromArgb(0x13, 0x2D, 0x22);

        public static readonly Color SecondaryBtnBg = Color.FromArgb(0x24, 0x31, 0x3D);
        public static readonly Color SecondaryBtnHover = Color.FromArgb(0x2E, 0x3E, 0x4D);
        public static readonly Color SecondaryBtnPressed = Color.FromArgb(0x1C, 0x27, 0x31);
        public static readonly Color SecondaryBtnBorder = Color.FromArgb(0x35, 0x47, 0x59);

        public static readonly Color TextPrimary = Color.FromArgb(0xE8, 0xEE, 0xF4);
        public static readonly Color TextSecondary = Color.FromArgb(0x93, 0xA6, 0xB8);
        public static readonly Color TextDim = Color.FromArgb(0x68, 0x7B, 0x8E);
        public static readonly Color GoldAccent = Color.FromArgb(0xEB, 0xC2, 0x47);
        public static readonly Color WarnAmber = Color.FromArgb(0xE6, 0x9F, 0x3E);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static float DetectDpiScale(Control control)
        {
            try
            {
                using (Graphics g = control != null ? control.CreateGraphics() : Graphics.FromHwnd(IntPtr.Zero))
                {
                    if (g.DpiX > 0f)
                    {
                        float s = g.DpiX / 96f;
                        if (s < 1f) return 1f;
                        if (s > 3.5f) return 3.5f;
                        return s;
                    }
                }
            }
            catch
            {
            }
            return 1f;
        }

        public static void ClampWindowToWorkingArea(Form form, int marginPx, bool shrinkToFit)
        {
            if (form == null)
            {
                return;
            }
            try
            {
                Screen scr = form.Owner != null
                    ? Screen.FromControl(form.Owner)
                    : Screen.FromControl(form);
                if (scr == null)
                {
                    scr = Screen.FromControl(form);
                }
                if (scr == null)
                {
                    scr = Screen.PrimaryScreen;
                }
                if (scr == null)
                {
                    return;
                }

                Rectangle wa = scr.WorkingArea;
                int w = form.Width;
                int h = form.Height;

                if (shrinkToFit)
                {
                    int maxW = Math.Max(360, wa.Width - marginPx * 2);
                    int maxH = Math.Max(320, wa.Height - marginPx * 2);
                    if (w > maxW) w = maxW;
                    if (h > maxH) h = maxH;
                }

                int x = wa.Left + (wa.Width - w) / 2;
                int y = wa.Top + (wa.Height - h) / 2;

                if (x + w > wa.Right - marginPx)
                {
                    x = wa.Right - marginPx - w;
                }
                if (x < wa.Left + marginPx)
                {
                    x = Math.Max(wa.Left, wa.Left + (wa.Width - w) / 2);
                    if (x < wa.Left) x = wa.Left;
                }

                if (y + h > wa.Bottom - marginPx)
                {
                    y = wa.Bottom - marginPx - h;
                }
                if (y < wa.Top + marginPx)
                {
                    y = Math.Max(wa.Top, wa.Top + (wa.Height - h) / 2);
                    if (y < wa.Top) y = wa.Top;
                }

                form.Bounds = new Rectangle(x, y, w, h);
            }
            catch
            {
            }
        }

        public static void EnsureWithinWorkingArea(Form form, int marginPx, bool shrinkToFit)
        {
            if (form == null)
            {
                return;
            }
            try
            {
                Screen scr = Screen.FromControl(form);
                if (scr == null)
                {
                    scr = Screen.PrimaryScreen;
                }
                if (scr == null)
                {
                    return;
                }
                Rectangle wa = scr.WorkingArea;
                if (form.Left < wa.Left
                    || form.Top < wa.Top
                    || form.Right > wa.Right - marginPx
                    || form.Bottom > wa.Bottom - marginPx)
                {
                    ClampWindowToWorkingArea(form, marginPx, shrinkToFit);
                }
            }
            catch
            {
            }
        }

        public static GraphicsPath CreateRoundedRect(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0 || r.Width <= 2 || r.Height <= 2)
            {
                path.AddRectangle(r);
                return path;
            }
            int d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawCard(Graphics g, Rectangle rect, Color fill, Color border, int radius)
        {
            Rectangle borderRect = new Rectangle(rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
            using (GraphicsPath path = CreateRoundedRect(borderRect, radius))
            using (SolidBrush b = new SolidBrush(fill))
            using (Pen p = new Pen(border, 1f))
            {
                g.FillPath(b, path);
                g.DrawPath(p, path);
            }
        }

        public static Icon CreateWindowIcon()
        {
            try
            {
                using (Bitmap bmp = new Bitmap(64, 64))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.Transparent);
                        DrawTacticalRadarBadge(g, new Rectangle(2, 2, 60, 60));
                    }
                    IntPtr hIcon = bmp.GetHicon();
                    try
                    {
                        using (Icon tmp = Icon.FromHandle(hIcon))
                        {
                            return (Icon)tmp.Clone();
                        }
                    }
                    finally
                    {
                        DestroyIcon(hIcon);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static void DrawTacticalRadarBadge(Graphics g, Rectangle bounds)
        {
            DrawCard(g, bounds, RecessedBg, BorderEmerald, Math.Max(6, bounds.Width / 6));

            float cx = bounds.X + bounds.Width * 0.5f;
            float cy = bounds.Y + bounds.Height * 0.5f;
            float rOuter = bounds.Width * 0.33f;
            float rInner = bounds.Width * 0.17f;

            using (Pen ringOuter = new Pen(Color.FromArgb(190, EmeraldPrimary), Math.Max(1.5f, bounds.Width * 0.04f)))
            using (Pen ringInner = new Pen(Color.FromArgb(110, EmeraldPrimary), Math.Max(1f, bounds.Width * 0.03f)))
            using (Pen crossPen = new Pen(EmeraldPrimary, Math.Max(1.5f, bounds.Width * 0.04f)))
            using (SolidBrush pipBrush = new SolidBrush(EmeraldPrimary))
            {
                g.DrawEllipse(ringOuter, cx - rOuter, cy - rOuter, rOuter * 2f, rOuter * 2f);
                g.DrawEllipse(ringInner, cx - rInner, cy - rInner, rInner * 2f, rInner * 2f);

                float tick = rOuter + bounds.Width * 0.07f;
                float gap = bounds.Width * 0.08f;
                g.DrawLine(crossPen, cx - tick, cy, cx - gap, cy);
                g.DrawLine(crossPen, cx + gap, cy, cx + tick, cy);
                g.DrawLine(crossPen, cx, cy - tick, cx, cy - gap);
                g.DrawLine(crossPen, cx, cy + gap, cx, cy + tick);

                float dotR = Math.Max(2f, bounds.Width * 0.055f);
                g.FillEllipse(pipBrush, cx - dotR, cy - dotR, dotR * 2f, dotR * 2f);
            }
        }

    }

    internal sealed class TacticalButton : Control
    {
        public bool IsPrimary;
        public bool IsSubtleLink;
        public int CornerRadius = 6;
        public Color SurfaceBehind = Color.Empty;
        private bool _hovered;
        private bool _pressed;

        public TacticalButton()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Enabled)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_pressed)
            {
                _pressed = false;
                Invalidate();
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
            {
                OnClick(EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color clearBg = SurfaceBehind != Color.Empty
                ? SurfaceBehind
                : (Parent != null ? Parent.BackColor : TacticalTheme.BgDeep);
            g.Clear(clearBg);

            Rectangle r = new Rectangle(0, 0, Width, Height);
            Color fill;
            Color border;
            Color fg;

            if (!Enabled)
            {
                fill = Color.FromArgb(0x18, 0x20, 0x28);
                border = Color.FromArgb(0x25, 0x31, 0x3D);
                fg = TacticalTheme.TextDim;
            }
            else if (IsPrimary)
            {
                fill = _pressed ? TacticalTheme.EmeraldPressed : (_hovered ? TacticalTheme.EmeraldHover : TacticalTheme.EmeraldPrimary);
                border = fill;
                fg = TacticalTheme.EmeraldDarkText;
            }
            else if (IsSubtleLink)
            {
                fill = _pressed ? TacticalTheme.SecondaryBtnPressed : (_hovered ? TacticalTheme.CardSurface : TacticalTheme.BgDeep);
                border = _hovered ? TacticalTheme.SecondaryBtnBorder : TacticalTheme.BorderCrisp;
                fg = _hovered ? TacticalTheme.TextPrimary : TacticalTheme.TextSecondary;
            }
            else
            {
                fill = _pressed ? TacticalTheme.SecondaryBtnPressed : (_hovered ? TacticalTheme.SecondaryBtnHover : TacticalTheme.SecondaryBtnBg);
                border = _hovered ? Color.FromArgb(0x46, 0x5D, 0x73) : TacticalTheme.SecondaryBtnBorder;
                fg = TacticalTheme.TextPrimary;
            }

            TacticalTheme.DrawCard(g, r, fill, border, CornerRadius);

            TextFormatFlags flags = TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix;
            Rectangle textRect = new Rectangle(4, _pressed ? 1 : 0, Width - 8, Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fg, flags);
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly PayloadData _payload;
        private readonly string _appBaseDir;
        private readonly CliMode _launchMode;
        private readonly float _scale;

        private string _steamDir;
        private bool _isBusy;
        private bool _uacAutoTriggered;

        private string _statusBadgeText = string.Empty;
        private Color _statusBadgeFg = TacticalTheme.TextSecondary;
        private Color _statusBadgeBg = TacticalTheme.RecessedBg;
        private Color _statusBadgeBorder = TacticalTheme.BorderCrisp;

        private Font _fontBrand;
        private Font _fontBadge;
        private Font _fontSub;
        private Font _fontSection;
        private Font _fontBody;
        private Font _fontSmall;
        private Font _fontPrimaryBtn;
        private Font _fontSecondaryBtn;
        private Font _fontLog;
        private Icon _winIcon;

        private Rectangle _headerRect;
        private Rectangle _pathCardRect;
        private Rectangle _pathInputRect;
        private Rectangle _logCardRect;

        private TextBox _pathBox;
        private TacticalButton _browseBtn;
        private TacticalButton _installBtn;
        private TacticalButton _uninstallBtn;
        private TextBox _logBox;
        private TacticalButton _supportLinkBtn;
        private TacticalButton _boostyLinkBtn;
        private TacticalButton _githubLinkBtn;

        public SetupForm(
            PayloadData payload,
            string appBaseDir,
            string explicitGameDir,
            string explicitSteamDir,
            CliMode launchMode = CliMode.DefaultGui)
        {
            _payload = payload;
            _appBaseDir = appBaseDir;
            _launchMode = launchMode;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint,
                true);

            AutoScaleMode = AutoScaleMode.Dpi;
            _scale = TacticalTheme.DetectDpiScale(this);

            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = TacticalTheme.BgDeep;
            ForeColor = TacticalTheme.TextPrimary;
            Text = "Zombies Aren't Real - Русификатор v1.3";
            ClientSize = new Size(S(728), S(414));

            _winIcon = TacticalTheme.CreateWindowIcon();
            if (_winIcon != null)
            {
                Icon = _winIcon;
            }

            InitFonts();
            BuildControls();
            InitDetection(explicitGameDir, explicitSteamDir);
            ActiveControl = _installBtn;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            TacticalTheme.ClampWindowToWorkingArea(this, S(12), false);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TacticalTheme.EnsureWithinWorkingArea(this, S(12), false);

            if (!_uacAutoTriggered)
            {
                _uacAutoTriggered = true;
                if (_launchMode == CliMode.UacInstallGui)
                {
                    BeginInvoke((MethodInvoker)delegate { StartInstallFlow(true); });
                }
                else if (_launchMode == CliMode.UacUninstallGui)
                {
                    BeginInvoke((MethodInvoker)delegate { StartUninstallFlow(true); });
                }
            }
        }

        private int S(int px)
        {
            return (int)Math.Round(px * _scale);
        }

        private void InitFonts()
        {
            _fontBrand = new Font("Segoe UI", S(20), FontStyle.Bold, GraphicsUnit.Pixel);
            _fontBadge = new Font("Segoe UI", S(11), FontStyle.Bold, GraphicsUnit.Pixel);
            _fontSub = new Font("Segoe UI", S(12), FontStyle.Regular, GraphicsUnit.Pixel);
            _fontSection = new Font("Segoe UI", S(11), FontStyle.Bold, GraphicsUnit.Pixel);
            _fontBody = new Font("Segoe UI", S(13), FontStyle.Regular, GraphicsUnit.Pixel);
            _fontSmall = new Font("Segoe UI", S(11), FontStyle.Regular, GraphicsUnit.Pixel);
            _fontPrimaryBtn = new Font("Segoe UI", S(13), FontStyle.Bold, GraphicsUnit.Pixel);
            _fontSecondaryBtn = new Font("Segoe UI", S(13), FontStyle.Bold, GraphicsUnit.Pixel);
            _fontLog = new Font("Consolas", S(12), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private void BuildControls()
        {
            int pad = S(20);
            int contentW = ClientSize.Width - pad * 2;

            _headerRect = new Rectangle(pad, S(16), contentW, S(82));
            _pathCardRect = new Rectangle(pad, S(110), contentW, S(82));

            int browseW = S(114);
            int inputX = _pathCardRect.X + S(16);
            int inputY = _pathCardRect.Y + S(33);
            int inputH = S(36);
            int inputW = _pathCardRect.Width - S(32) - browseW - S(10);
            _pathInputRect = new Rectangle(inputX, inputY, inputW, inputH);

            _pathBox = new TextBox();
            _pathBox.Multiline = true;
            _pathBox.WordWrap = false;
            _pathBox.AcceptsReturn = false;
            _pathBox.BorderStyle = BorderStyle.None;
            _pathBox.BackColor = TacticalTheme.RecessedBg;
            _pathBox.ForeColor = TacticalTheme.TextPrimary;
            _pathBox.Font = _fontBody;
            int lineH = _fontBody.Height + S(2);
            int tbTop = inputY + Math.Max(S(4), (inputH - lineH) / 2);
            _pathBox.Bounds = new Rectangle(inputX + S(10), tbTop, inputW - S(20), lineH);
            _pathBox.KeyDown += delegate(object s, KeyEventArgs ke)
            {
                if (ke.KeyCode == Keys.Enter || ke.KeyCode == Keys.Return)
                {
                    ke.SuppressKeyPress = true;
                    ke.Handled = true;
                }
            };
            _pathBox.TextChanged += delegate
            {
                string raw = _pathBox.Text ?? string.Empty;
                if (raw.IndexOf('\r') >= 0 || raw.IndexOf('\n') >= 0)
                {
                    _pathBox.Text = raw.Replace("\r", string.Empty).Replace("\n", string.Empty);
                    return;
                }
                RefreshUiState();
            };
            Controls.Add(_pathBox);

            _browseBtn = new TacticalButton();
            _browseBtn.Text = "Обзор...";
            _browseBtn.Font = _fontSecondaryBtn;
            _browseBtn.CornerRadius = S(6);
            _browseBtn.SurfaceBehind = TacticalTheme.CardSurface;
            _browseBtn.Bounds = new Rectangle(inputX + inputW + S(10), inputY, browseW, inputH);
            _browseBtn.Click += OnBrowseClicked;
            Controls.Add(_browseBtn);

            int btnY = S(206);
            int btnH = S(44);
            int gap = S(12);
            int restoreW = S(240);
            int installW = contentW - restoreW - gap;

            _installBtn = new TacticalButton();
            _installBtn.Text = "Установить русификатор";
            _installBtn.Font = _fontPrimaryBtn;
            _installBtn.IsPrimary = true;
            _installBtn.CornerRadius = S(6);
            _installBtn.Bounds = new Rectangle(pad, btnY, installW, btnH);
            _installBtn.Click += OnInstallClicked;
            Controls.Add(_installBtn);

            _uninstallBtn = new TacticalButton();
            _uninstallBtn.Text = "Вернуть оригинал";
            _uninstallBtn.Font = _fontSecondaryBtn;
            _uninstallBtn.CornerRadius = S(6);
            _uninstallBtn.Bounds = new Rectangle(pad + installW + gap, btnY, restoreW, btnH);
            _uninstallBtn.Click += OnUninstallClicked;
            Controls.Add(_uninstallBtn);

            _logCardRect = new Rectangle(pad, S(264), contentW, S(96));
            _logBox = new TextBox();
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.ScrollBars = ScrollBars.None;
            _logBox.BackColor = TacticalTheme.RecessedBg;
            _logBox.ForeColor = Color.FromArgb(0xD0, 0xDC, 0xE8);
            _logBox.Font = _fontLog;
            _logBox.Location = new Point(_logCardRect.X + S(16), _logCardRect.Y + S(33));
            _logBox.Size = new Size(_logCardRect.Width - S(32), _logCardRect.Height - S(46));
            Controls.Add(_logBox);

            int footerY = S(372);
            int footerH = S(28);
            int ghW = S(88);
            int boostyW = S(88);
            int supW = S(168);

            _githubLinkBtn = new TacticalButton();
            _githubLinkBtn.Text = "GitHub";
            _githubLinkBtn.Font = _fontSmall;
            _githubLinkBtn.IsSubtleLink = true;
            _githubLinkBtn.CornerRadius = S(5);
            _githubLinkBtn.Bounds = new Rectangle(ClientSize.Width - pad - ghW, footerY, ghW, footerH);
            _githubLinkBtn.Click += delegate { Program.OpenBrowserUrl(Program.GithubUrl); };
            Controls.Add(_githubLinkBtn);

            _boostyLinkBtn = new TacticalButton();
            _boostyLinkBtn.Text = "Boosty";
            _boostyLinkBtn.Font = _fontSmall;
            _boostyLinkBtn.IsSubtleLink = true;
            _boostyLinkBtn.CornerRadius = S(5);
            _boostyLinkBtn.Bounds = new Rectangle(ClientSize.Width - pad - ghW - S(8) - boostyW, footerY, boostyW, footerH);
            _boostyLinkBtn.Click += delegate { Program.OpenBrowserUrl(Program.BoostyUrl); };
            Controls.Add(_boostyLinkBtn);

            _supportLinkBtn = new TacticalButton();
            _supportLinkBtn.Text = "Поддержать перевод";
            _supportLinkBtn.Font = _fontSmall;
            _supportLinkBtn.IsSubtleLink = true;
            _supportLinkBtn.CornerRadius = S(5);
            _supportLinkBtn.Bounds = new Rectangle(ClientSize.Width - pad - ghW - S(8) - boostyW - S(8) - supW, footerY, supW, footerH);
            _supportLinkBtn.Click += delegate { Program.OpenBrowserUrl(Program.SupportUrl); };
            Controls.Add(_supportLinkBtn);
        }

        private void InitDetection(string explicitGameDir, string explicitSteamDir)
        {
            _steamDir = SteamLocator.DetectSteamDirectory(explicitSteamDir, explicitGameDir);
            string detectedGame = !string.IsNullOrWhiteSpace(explicitGameDir)
                ? SteamLocator.NormalizePickedGameDirectory(explicitGameDir)
                : SteamLocator.AutoDetectGameDirectory(_steamDir, _appBaseDir);

            if (!string.IsNullOrEmpty(detectedGame))
            {
                _pathBox.Text = detectedGame;
                if (string.IsNullOrEmpty(_steamDir))
                {
                    _steamDir = SteamLocator.DetectSteamDirectory(null, detectedGame);
                }
            }

            RefreshUiState();

            if (SteamLocator.IsValidGameDirectory(_pathBox.Text))
            {
                AppendLog(InstallerService.IsRussifierInstalled(_pathBox.Text)
                    ? "Русификатор установлен."
                    : "Готов к установке.");
            }
            else
            {
                AppendLog("Выберите папку с игрой кнопкой «Обзор...».");
            }
        }

        private void RefreshUiState()
        {
            string currentGameDir = (_pathBox.Text ?? string.Empty).Trim();
            bool validGame = SteamLocator.IsValidGameDirectory(currentGameDir);
            if (validGame && string.IsNullOrEmpty(_steamDir))
            {
                _steamDir = SteamLocator.DetectSteamDirectory(null, currentGameDir);
            }

            bool hasBackup = validGame && BackupManifestStore.HasValidBackup(currentGameDir);
            bool installed = validGame && InstallerService.IsRussifierInstalled(currentGameDir);

            if (!validGame)
            {
                _statusBadgeText = "Укажите папку игры";
                _statusBadgeFg = TacticalTheme.WarnAmber;
                _statusBadgeBg = Color.FromArgb(0x2B, 0x21, 0x14);
                _statusBadgeBorder = Color.FromArgb(0x5E, 0x45, 0x22);
            }
            else if (installed)
            {
                _statusBadgeText = "Установлен";
                _statusBadgeFg = TacticalTheme.EmeraldPrimary;
                _statusBadgeBg = TacticalTheme.EmeraldPillBg;
                _statusBadgeBorder = TacticalTheme.BorderEmerald;
            }
            else
            {
                _statusBadgeText = "Не установлен";
                _statusBadgeFg = TacticalTheme.TextSecondary;
                _statusBadgeBg = TacticalTheme.RecessedBg;
                _statusBadgeBorder = TacticalTheme.BorderCrisp;
            }

            _installBtn.Text = installed ? "Обновить русификатор" : "Установить русификатор";
            _installBtn.Enabled = !_isBusy && validGame;
            _uninstallBtn.Enabled = !_isBusy && hasBackup;
            _browseBtn.Enabled = !_isBusy;
            Invalidate();
        }

        private void OnBrowseClicked(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Выберите папку установки Zombies Aren't Real (содержащую zombies-arent-real.exe)";
                dlg.ShowNewFolderButton = false;
                if (SteamLocator.SafeDirectoryExists(_pathBox.Text))
                {
                    dlg.SelectedPath = _pathBox.Text;
                }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    string picked = SteamLocator.NormalizePickedGameDirectory(dlg.SelectedPath);
                    _pathBox.Text = picked;
                    RefreshUiState();
                    if (SteamLocator.IsValidGameDirectory(picked))
                    {
                        AppendLog("Выбрана папка игры: " + picked);
                    }
                    else
                    {
                        AppendLog("В выбранной папке не найден zombies-arent-real.exe: " + picked);
                    }
                }
            }
        }

        private void OnInstallClicked(object sender, EventArgs e)
        {
            StartInstallFlow(_launchMode == CliMode.UacInstallGui);
        }

        private void OnUninstallClicked(object sender, EventArgs e)
        {
            StartUninstallFlow(_launchMode == CliMode.UacUninstallGui);
        }

        private void StartInstallFlow(bool skipUacRelaunch)
        {
            if (_isBusy)
            {
                return;
            }
            string gameDir = SteamLocator.NormalizePickedGameDirectory(_pathBox.Text);
            string steamDir = _steamDir;
            if (!CheckReadyBeforeAction(gameDir))
            {
                return;
            }
            if (!skipUacRelaunch && ElevationHelper.NeedsElevationForGameDir(gameDir))
            {
                TryElevateAndClose("--uac-install", gameDir, steamDir);
                return;
            }

            string skippedSchema = null;
            RunWorkerAction(
                "Установка русификатора...",
                delegate(Action<string> logger)
                {
                    InstallerService.Install(_payload, gameDir, steamDir, logger, out skippedSchema);
                },
                delegate(Exception failure)
                {
                    HandleWorkerFinished(failure, skippedSchema, true, skipUacRelaunch, gameDir, steamDir);
                });
        }

        private void StartUninstallFlow(bool skipUacRelaunch)
        {
            if (_isBusy)
            {
                return;
            }
            string gameDir = SteamLocator.NormalizePickedGameDirectory(_pathBox.Text);
            string steamDir = _steamDir;
            if (!CheckReadyBeforeAction(gameDir))
            {
                return;
            }
            if (!skipUacRelaunch && (ElevationHelper.NeedsElevationForGameDir(gameDir)))
            {
                TryElevateAndClose("--uac-uninstall", gameDir, steamDir);
                return;
            }

            string skippedSchema = null;
            RunWorkerAction(
                "Возврат оригинальных файлов...",
                delegate(Action<string> logger)
                {
                    InstallerService.Uninstall(gameDir, steamDir, logger, out skippedSchema);
                },
                delegate(Exception failure)
                {
                    HandleWorkerFinished(failure, skippedSchema, false, skipUacRelaunch, gameDir, steamDir);
                });
        }

        private bool CheckReadyBeforeAction(string gameDir)
        {
            if (!SteamLocator.IsValidGameDirectory(gameDir))
            {
                string msg = "Папка игры не содержит zombies-arent-real.exe и lang\\en.lang: " + gameDir;
                AppendLog("ОШИБКА: " + msg);
                MessageBox.Show(this, msg, "Папка игры не найдена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                SteamLocator.AssertGameClosed(gameDir);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("ОШИБКА: " + ex.Message);
                MessageBox.Show(this, ex.Message, "Игра запущена", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        private bool TryElevateAndClose(string modeFlag, string gameDir, string steamDir)
        {
            AppendLog("Запрос прав администратора Windows (UAC)...");
            bool cancelled;
            string errorMsg;
            if (ElevationHelper.TryRelaunchElevated(modeFlag, gameDir, steamDir, out cancelled, out errorMsg))
            {
                Close();
                return true;
            }
            if (cancelled)
            {
                AppendLog("Запрос прав администратора отменён пользователем.");
            }
            else if (!string.IsNullOrEmpty(errorMsg))
            {
                AppendLog("Не удалось запустить с правами администратора: " + errorMsg);
                MessageBox.Show(this, errorMsg, "Ошибка UAC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshUiState();
            return false;
        }

        private void HandleWorkerFinished(
            Exception failure,
            string skippedSchema,
            bool isInstall,
            bool skipUacRelaunch,
            string gameDir,
            string steamDir)
        {
            string uacFlag = isInstall ? "--uac-install" : "--uac-uninstall";
            if (failure is UnauthorizedAccessException && !skipUacRelaunch && !ElevationHelper.IsProcessElevated())
            {
                TryElevateAndClose(uacFlag, gameDir, steamDir);
                return;
            }
            if (failure != null)
            {
                AppendLog("ОШИБКА: " + failure.Message);
                MessageBox.Show(this, failure.Message, "Ошибка операции", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        private void RunWorkerAction(string startHeader, Action<Action<string>> work, Action<Exception> onCompleted)
        {
            _isBusy = true;
            RefreshUiState();
            AppendLog("--------------------------------------------------");
            AppendLog(startHeader);

            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception failure = null;
                try
                {
                    work(delegate(string line)
                    {
                        SafeInvoke(delegate { AppendLog(line); });
                    });
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                SafeInvoke(delegate
                {
                    _isBusy = false;
                    RefreshUiState();
                    if (onCompleted != null)
                    {
                        onCompleted(failure);
                    }
                });
            });
        }

        private void SafeInvoke(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch
            {
            }
        }

        private void AppendLog(string message)
        {
            string stamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            string line = "[" + stamp + "] " + message;
            if (_logBox.TextLength > 0)
            {
                _logBox.AppendText("\r\n" + line);
            }
            else
            {
                _logBox.Text = line;
            }
            if (_logBox.ScrollBars == ScrollBars.None && _logBox.Lines.Length > 3)
            {
                _logBox.ScrollBars = ScrollBars.Vertical;
            }
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            PaintHeaderCard(g);
            PaintPathCard(g);
            PaintLogCard(g);
            PaintFooter(g);
        }

        private void PaintHeaderCard(Graphics g)
        {
            TacticalTheme.DrawCard(g, _headerRect, TacticalTheme.CardSurface, TacticalTheme.BorderCrisp, S(8));

            Rectangle emblemRect = new Rectangle(_headerRect.X + S(16), _headerRect.Y + S(15), S(52), S(52));
            TacticalTheme.DrawTacticalRadarBadge(g, emblemRect);

            int textX = emblemRect.Right + S(14);
            TextRenderer.DrawText(
                g,
                "ZOMBIES AREN'T REAL",
                _fontBrand,
                new Point(textX, _headerRect.Y + S(15)),
                TacticalTheme.TextPrimary,
                TextFormatFlags.NoPrefix);

            TextRenderer.DrawText(
                g,
                "Текст и шрифты для версии игры 0.44.0",
                _fontSub,
                new Point(textX, _headerRect.Y + S(46)),
                TacticalTheme.TextSecondary,
                TextFormatFlags.NoPrefix);

            if (!string.IsNullOrEmpty(_statusBadgeText))
            {
                Size stSize = TextRenderer.MeasureText(g, _statusBadgeText, _fontBadge, new Size(S(260), S(30)), TextFormatFlags.NoPrefix);
                int pillW = stSize.Width + S(26);
                int pillH = S(26);
                Rectangle stPill = new Rectangle(
                    _headerRect.Right - S(16) - pillW,
                    _headerRect.Y + (_headerRect.Height - pillH) / 2,
                    pillW,
                    pillH);
                TacticalTheme.DrawCard(g, stPill, _statusBadgeBg, _statusBadgeBorder, S(13));

                int dotSize = S(7);
                using (SolidBrush dotBrush = new SolidBrush(_statusBadgeFg))
                {
                    g.FillEllipse(dotBrush, stPill.X + S(10), stPill.Y + (pillH - dotSize) / 2, dotSize, dotSize);
                }
                Rectangle labelRect = new Rectangle(stPill.X + S(20), stPill.Y, stPill.Width - S(24), stPill.Height);
                TextRenderer.DrawText(
                    g,
                    _statusBadgeText,
                    _fontBadge,
                    labelRect,
                    _statusBadgeFg,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
            }
        }

        private void PaintPathCard(Graphics g)
        {
            TacticalTheme.DrawCard(g, _pathCardRect, TacticalTheme.CardSurface, TacticalTheme.BorderCrisp, S(8));

            TextRenderer.DrawText(
                g,
                "ПАПКА С ИГРОЙ",
                _fontSection,
                new Point(_pathCardRect.X + S(16), _pathCardRect.Y + S(11)),
                TacticalTheme.TextSecondary,
                TextFormatFlags.NoPrefix);

            TacticalTheme.DrawCard(g, _pathInputRect, TacticalTheme.RecessedBg, TacticalTheme.BorderCrisp, S(5));
        }

        private void PaintLogCard(Graphics g)
        {
            TacticalTheme.DrawCard(g, _logCardRect, TacticalTheme.CardSurface, TacticalTheme.BorderCrisp, S(8));

            TextRenderer.DrawText(
                g,
                "ЖУРНАЛ ОПЕРАЦИЙ",
                _fontSection,
                new Point(_logCardRect.X + S(16), _logCardRect.Y + S(9)),
                TacticalTheme.TextSecondary,
                TextFormatFlags.NoPrefix);

            Rectangle logInner = new Rectangle(
                _logCardRect.X + S(12),
                _logCardRect.Y + S(28),
                _logCardRect.Width - S(24),
                _logCardRect.Height - S(38));
            TacticalTheme.DrawCard(g, logInner, TacticalTheme.RecessedBg, TacticalTheme.BorderLocked, S(5));
        }

        private void PaintFooter(Graphics g)
        {
            Rectangle leftFooter = new Rectangle(S(22), S(372), S(260), S(28));
            TextRenderer.DrawText(
                g,
                "v1.3 · neoru-droid",
                _fontSmall,
                leftFooter,
                TacticalTheme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fontBrand != null) _fontBrand.Dispose();
                if (_fontBadge != null) _fontBadge.Dispose();
                if (_fontSub != null) _fontSub.Dispose();
                if (_fontSection != null) _fontSection.Dispose();
                if (_fontBody != null) _fontBody.Dispose();
                if (_fontSmall != null) _fontSmall.Dispose();
                if (_fontPrimaryBtn != null) _fontPrimaryBtn.Dispose();
                if (_fontSecondaryBtn != null) _fontSecondaryBtn.Dispose();
                if (_fontLog != null) _fontLog.Dispose();
                if (_winIcon != null) _winIcon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
