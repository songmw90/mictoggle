using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MicToggle;

internal static class ProcessTreeSnapshot
{
    private const uint SnapshotProcesses = 0x00000002;
    private const int ErrorNoMoreFiles = 18;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    public static HashSet<int> GetDescendantProcessIds(int rootProcessId)
    {
        return FindDescendants(rootProcessId, CaptureParentProcessIds());
    }

    internal static HashSet<int> FindDescendants(
        int rootProcessId,
        IReadOnlyDictionary<int, int> parentByProcessId)
    {
        var childrenByParent = new Dictionary<int, List<int>>();
        foreach (var (processId, parentProcessId) in parentByProcessId)
        {
            if (!childrenByParent.TryGetValue(parentProcessId, out var children))
            {
                children = [];
                childrenByParent.Add(parentProcessId, children);
            }

            children.Add(processId);
        }

        var descendants = new HashSet<int> { rootProcessId };
        var pending = new Queue<int>();
        pending.Enqueue(rootProcessId);
        while (pending.TryDequeue(out var parentProcessId))
        {
            if (!childrenByParent.TryGetValue(parentProcessId, out var children))
            {
                continue;
            }

            foreach (var childProcessId in children)
            {
                if (descendants.Add(childProcessId))
                {
                    pending.Enqueue(childProcessId);
                }
            }
        }

        return descendants;
    }

    private static Dictionary<int, int> CaptureParentProcessIds()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == InvalidHandleValue)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var parents = new Dictionary<int, int>();
            var entry = new ProcessEntry32
            {
                Size = (uint)Marshal.SizeOf<ProcessEntry32>(),
            };
            if (!Process32First(snapshot, ref entry))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorNoMoreFiles)
                {
                    return parents;
                }

                throw new Win32Exception(error);
            }

            do
            {
                parents[(int)entry.ProcessId] = (int)entry.ParentProcessId;
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
            }
            while (Process32Next(snapshot, ref entry));

            var finalError = Marshal.GetLastWin32Error();
            if (finalError != ErrorNoMoreFiles)
            {
                throw new Win32Exception(finalError);
            }

            return parents;
        }
        finally
        {
            _ = CloseHandle(snapshot);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }
}
