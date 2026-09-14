using ShadePilot.DriverEnhancement;

if (args.Length != 1)
{
    Console.Error.WriteLine("用法：WriteGuardTest <ShadePilot.DriverControl.exe>");
    return 2;
}

var provider = new IntelSharpnessControlProvider(args[0]);
var result = await provider.SetAsync(new SharpnessWriteRequest(0, true, 1, 6, Path.Combine(Path.GetTempPath(), "shadepilot-rejected-backup.txt")));
Console.WriteLine($"Succeeded={result.Succeeded}");
Console.WriteLine($"ExitCode={result.ExitCode}");
Console.WriteLine($"SetResult={result.SetResult}");
Console.WriteLine($"Error={result.Error}");
return result.Succeeded || result.ExitCode == 0 ? 1 : 0;
