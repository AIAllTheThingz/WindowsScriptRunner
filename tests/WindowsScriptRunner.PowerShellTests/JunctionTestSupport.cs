using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace WindowsScriptRunner.PowerShellTests;

internal static partial class JunctionTestSupport
{
    private const uint IoReparseTagMountPoint = 0xA0000003;
    private const uint FsctlSetReparsePoint = 0x000900A4;
    private const uint GenericWrite = 0x40000000;
    private const uint ShareReadWriteDelete = 0x00000007;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagBackupSemantics = 0x02000000;

    public static void Create(string junctionPath, string targetPath)
    {
        Directory.CreateDirectory(junctionPath);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        var substituteName = Encoding.Unicode.GetBytes(@"\??\" + target);
        var printName = Encoding.Unicode.GetBytes(target);
        var pathBufferLength =
            substituteName.Length + sizeof(ushort) + printName.Length + sizeof(ushort);
        var buffer = new byte[16 + pathBufferLength];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), IoReparseTagMountPoint);
        BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(4, 2),
            checked((ushort)(8 + pathBufferLength)));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8, 2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(10, 2),
            checked((ushort)substituteName.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(12, 2),
            checked((ushort)(substituteName.Length + sizeof(ushort))));
        BinaryPrimitives.WriteUInt16LittleEndian(
            buffer.AsSpan(14, 2),
            checked((ushort)printName.Length));
        substituteName.CopyTo(buffer, 16);
        printName.CopyTo(buffer, 16 + substituteName.Length + sizeof(ushort));

        using var handle = new SafeFileHandle(
            CreateFile(
                junctionPath,
                GenericWrite,
                ShareReadWriteDelete,
                nint.Zero,
                OpenExisting,
                FileFlagOpenReparsePoint | FileFlagBackupSemantics,
                nint.Zero),
            ownsHandle: true);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var nativeBuffer = Marshal.AllocHGlobal(buffer.Length);
        try
        {
            Marshal.Copy(buffer, 0, nativeBuffer, buffer.Length);
            if (!DeviceIoControl(
                    handle,
                    FsctlSetReparsePoint,
                    nativeBuffer,
                    checked((uint)buffer.Length),
                    nint.Zero,
                    0,
                    out _,
                    nint.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
        }
        finally
        {
            Marshal.FreeHGlobal(nativeBuffer);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        nint securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        nint inputBuffer,
        uint inputBufferSize,
        nint outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        nint overlapped);
}
