using ShadePilot.DriverEnhancement;

if (args.Length != 1)
{
    Console.Error.WriteLine("用法：ReadTest <ShadePilot.DriverProbe.exe>");
    return 2;
}

var provider = new IntelSharpnessReadProvider(args[0]);
var result = await provider.ReadAsync();
Console.WriteLine($"Succeeded={result.Succeeded}");
Console.WriteLine($"Supported={result.IsSupported}");
Console.WriteLine($"Status={result.Status}");
if (result.Error is not null) Console.WriteLine($"Error={result.Error}");
foreach (var adapter in result.Adapters)
{
    Console.WriteLine($"Adapter={adapter.Name};Vendor={adapter.VendorId};Device={adapter.DeviceId};Displays={adapter.DisplayOutputs.Count}");
    foreach (var display in adapter.DisplayOutputs.Where(d => d.SharpnessCapsResult == "0x0"))
        Console.WriteLine($"SharpnessDisplay={display.Index};Caps={display.SharpnessCapsResult};Current={display.CurrentSharpnessResult};RangeCount={display.Filters.Count};CurrentIntensity={display.CurrentIntensity}");
}
return result.Succeeded && result.IsSupported ? 0 : 1;
