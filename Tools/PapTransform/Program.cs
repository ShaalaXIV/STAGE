using STAGE;

if (args.Length != 2)
    throw new ArgumentException("Usage: PapTransform <root> <vfx-game-path>");

int files = 0;
int references = 0;
foreach (string pap in Directory.EnumerateFiles(args[0], "*_loop.pap", SearchOption.AllDirectories))
{
    references += BeesKneesPapTmbPatcher.MakeSilentSingleVfxLoop(pap, args[1], "hopstep");
    files++;
}
Console.WriteLine($"Patched {files} loop PAP files and redirected {references} AVFX references.");
