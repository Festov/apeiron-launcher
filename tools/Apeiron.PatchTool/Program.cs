using Apeiron.Services;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: Apeiron.PatchTool <old.exe> <new.exe> <out.patch>");
    return 1;
}

BinaryPatchHelper.CreatePatch(args[0], args[1], args[2]);
Console.WriteLine("Wrote " + args[2]);
return 0;
