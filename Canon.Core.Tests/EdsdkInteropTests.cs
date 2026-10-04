using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Canon.Core;

namespace Canon.Core.Tests;

/// <summary>
/// P/Invoke contract: library name, and layouts checked against EDSDKTypes.h (13.20.21), x64.
/// </summary>
public class EdsdkInteropTests
{
    [Fact]
    public void Every_import_uses_the_cross_platform_library_name()
    {
        var imports = typeof(EDSDK).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(method => method.GetCustomAttribute<DllImportAttribute>())
            .OfType<DllImportAttribute>()
            .ToList();

        // Guards against a reflection change returning nothing, which would make the test pass vacuously.
        Assert.True(imports.Count > 50, $"{imports.Count} imports found");
        Assert.All(imports, import => Assert.Equal("EDSDK", import.Value));
    }

    [Fact]
    public void Structures_have_the_size_of_the_headers()
    {
        Assert.Equal(32, Marshal.SizeOf<EDSDK.FocusShiftSetting>());
        Assert.Equal(48, Marshal.SizeOf<EDSDK.EdsManualWBData>());
        Assert.Equal(524, Marshal.SizeOf<EDSDK.EdsPropertyDesc>());
        Assert.Equal(288, Marshal.SizeOf<EDSDK.EdsDirectoryItemInfo>());
    }

    [Fact]
    public void Manual_white_balance_data_is_serialized_with_its_whole_payload()
    {
        var data = new EDSDK.EdsManualWBData { Valid = 1, dataSize = 20, szCaption = "Studio", data = Enumerable.Range(1, 20).Select(i => (byte)i).ToArray() };

        var buffer = EDSDK.ConvertMWB(data);

        Assert.Equal(40 + 12 + 20, buffer.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(buffer));
        Assert.Equal(20u + 12, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)));
        Assert.Equal("Studio", Encoding.ASCII.GetString(buffer, 8, 6));
        Assert.All(buffer[40..52], b => Assert.Equal(0, b));
        Assert.Equal(data.data, buffer[52..]);
    }

    [Fact]
    public void Manual_white_balance_data_is_read_with_its_whole_payload()
    {
        var buffer = new byte[40 + 20];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 20);
        Encoding.ASCII.GetBytes("Studio").CopyTo(buffer, 8);
        for (var i = 0; i < 20; i++)
            buffer[40 + i] = (byte)(i + 1);

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            var data = EDSDK.MarshalPtrToManualWBData(handle.AddrOfPinnedObject());

            Assert.Equal(1u, data.Valid);
            Assert.Equal("Studio", data.szCaption);
            Assert.Equal(buffer[40..], data.data);
        }
        finally
        {
            handle.Free();
        }
    }
}
