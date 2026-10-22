using System.Runtime.InteropServices;

if (Environment.Version.Major != 11)
    throw new InvalidOperationException($"Unexpected runtime: {RuntimeInformation.FrameworkDescription}");

Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; architecture: {RuntimeInformation.ProcessArchitecture}");
var retained = new byte[4][];
await Task.WhenAll(Enumerable.Range(0, retained.Length).Select(worker => Task.Run(() =>
{
    var bytes = new byte[128 * 1024];
    Array.Fill(bytes, (byte)(worker + 1));
    retained[worker] = bytes;
    for (int i = 0; i < 4000; i++)
    {
        var scratch = new byte[i % 8 == 0 ? 96 * 1024 : 4096];
        scratch[0] = (byte)i;
        GC.KeepAlive(scratch);
    }
}))).ConfigureAwait(false);

var pinned = GCHandle.Alloc(retained[0], GCHandleType.Pinned);
try
{
    var address = pinned.AddrOfPinnedObject();
    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
    GC.WaitForPendingFinalizers();
    if (pinned.AddrOfPinnedObject() != address || Marshal.ReadByte(address) != 1)
        throw new InvalidOperationException("Pinned buffer changed during collection.");
    for (int worker = 0; worker < retained.Length; worker++)
    {
        if (retained[worker].Any(value => value != worker + 1))
            throw new InvalidOperationException("Retained data changed during collection.");
    }
}
finally
{
    pinned.Free();
}

Console.WriteLine("Runtime GC/async/pinning smoke test passed.");
