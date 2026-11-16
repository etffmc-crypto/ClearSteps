// ClearSteps - Windows audio endpoint helpers (enumerate, format, enable/disable, default).
// C# 5 compatible on purpose: builds with the csc.exe that ships in every Windows 10/11.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ClearSteps
{
    public class Endpoint
    {
        public string Id;          // {0.0.0.00000000}.{guid}
        public string Guid;        // {guid} lowercase with braces
        public string Name;        // "Art Tune +"
        public string Adapter;     // "VB-Audio Virtual Cable"
        public string FullName;    // "Art Tune + (VB-Audio Virtual Cable)"
        public bool IsRender;
        public int State;          // 1 active, 2 disabled, 4 not present, 8 unplugged
        public int Channels;
        public int Bits;
        public int SampleRate;
        public override string ToString()
        {
            return (IsRender ? "OUT " : "IN  ") + FullName + " state=" + State + " " + Channels + "ch/" + Bits + "bit/" + SampleRate + " " + Guid;
        }
    }

    public static class Audio
    {
        [StructLayout(LayoutKind.Sequential)]
        struct PROPERTYKEY { public Guid fmtid; public int pid; public PROPERTYKEY(string g, int p) { fmtid = new Guid(g); pid = p; } }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct PROPVARIANT
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public IntPtr p;
            [FieldOffset(8)] public int cb;        // for VT_BLOB
            [FieldOffset(16)] public IntPtr pBlob; // for VT_BLOB (x64 layout)
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
            [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }

        // Undocumented but stable since Windows 7; the Sound control panel itself uses it.
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int bDefault, out IntPtr format);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int bDefault, out long defaultPeriod, out long minPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, ref long period);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int bFxStore, ref PROPERTYKEY key, out PROPVARIANT value);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, int bFxStore, ref PROPERTYKEY key, ref PROPVARIANT value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
        }

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT pv);

        static readonly PROPERTYKEY PKEY_DeviceDesc = new PROPERTYKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 2);
        static readonly PROPERTYKEY PKEY_FriendlyName = new PROPERTYKEY("a45c254e-df1c-4efd-8020-67d146a850e0", 14);
        static readonly PROPERTYKEY PKEY_AdapterName = new PROPERTYKEY("026e516e-b814-414b-83cd-856d6fef4822", 2);
        static readonly PROPERTYKEY PKEY_DeviceFormat = new PROPERTYKEY("f19f064d-082c-4e27-bc73-6882a1bb8e4c", 0);

        public const int StateActive = 1, StateDisabled = 2, StateAll = 15;

        static string GetString(IPropertyStore store, PROPERTYKEY key)
        {
            PROPVARIANT pv;
            if (store.GetValue(ref key, out pv) != 0) return "";
            try { return pv.vt == 31 && pv.p != IntPtr.Zero ? Marshal.PtrToStringUni(pv.p) : ""; }
            finally { PropVariantClear(ref pv); }
        }

        public static List<Endpoint> List()
        {
            var result = new List<Endpoint>();
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            for (int flow = 0; flow <= 1; flow++)
            {
                IMMDeviceCollection col;
                if (en.EnumAudioEndpoints(flow, StateAll, out col) != 0) continue;
                int n; col.GetCount(out n);
                for (int i = 0; i < n; i++)
                {
                    IMMDevice dev; if (col.Item(i, out dev) != 0) continue;
                    var e = new Endpoint();
                    e.IsRender = flow == 0;
                    dev.GetId(out e.Id);
                    dev.GetState(out e.State);
                    int brace = e.Id.LastIndexOf('{');
                    e.Guid = brace >= 0 ? e.Id.Substring(brace).ToLowerInvariant() : "";
                    IPropertyStore store;
                    if (dev.OpenPropertyStore(0, out store) == 0)
                    {
                        e.Name = GetString(store, PKEY_DeviceDesc);
                        e.FullName = GetString(store, PKEY_FriendlyName);
                        e.Adapter = GetString(store, PKEY_AdapterName);
                        PROPVARIANT pv; var k = PKEY_DeviceFormat;
                        if (store.GetValue(ref k, out pv) == 0)
                        {
                            if (pv.vt == 65 && pv.pBlob != IntPtr.Zero && pv.cb >= 18)
                            {
                                e.Channels = Marshal.ReadInt16(pv.pBlob, 2);
                                e.SampleRate = Marshal.ReadInt32(pv.pBlob, 4);
                                e.Bits = Marshal.ReadInt16(pv.pBlob, 14);
                            }
                            PropVariantClear(ref pv);
                        }
                    }
                    if (string.IsNullOrEmpty(e.FullName)) e.FullName = e.Name + " (" + e.Adapter + ")";
                    result.Add(e);
                }
            }
            return result;
        }

        static readonly Guid SubtypePcm = new Guid("00000001-0000-0010-8000-00aa00389b71");

        // Builds a WAVEFORMATEXTENSIBLE (40 bytes) in unmanaged memory. Caller frees with Marshal.FreeHGlobal.
        static IntPtr MakeFormat(int channels, int bits, int rate, int channelMask)
        {
            int container = bits == 24 ? 24 : bits;
            int block = channels * container / 8;
            IntPtr p = Marshal.AllocHGlobal(40);
            Marshal.WriteInt16(p, 0, unchecked((short)0xFFFE));
            Marshal.WriteInt16(p, 2, (short)channels);
            Marshal.WriteInt32(p, 4, rate);
            Marshal.WriteInt32(p, 8, rate * block);
            Marshal.WriteInt16(p, 12, (short)block);
            Marshal.WriteInt16(p, 14, (short)container);
            Marshal.WriteInt16(p, 16, 22);
            Marshal.WriteInt16(p, 18, (short)bits);
            Marshal.WriteInt32(p, 20, channelMask);
            byte[] g = SubtypePcm.ToByteArray();
            Marshal.Copy(g, 0, IntPtr.Add(p, 24), 16);
            return p;
        }

        public static int DefaultMask(int channels)
        {
            switch (channels)
            {
                case 1: return 0x4;
                case 2: return 0x3;
                case 6: return 0x3F;
                case 8: return 0x63F;
                default: return 0;   // 16ch: no speaker positions
            }
        }

        /// Sets the shared-mode format of an endpoint (what Sound > Properties > Advanced does). Returns HRESULT.
        public static int SetFormat(string endpointId, int channels, int bits, int rate, int channelMask)
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            IntPtr fmt = MakeFormat(channels, bits, rate, channelMask);
            try { return pc.SetDeviceFormat(endpointId, fmt, fmt); }
            finally { Marshal.FreeHGlobal(fmt); }
        }

        /// Enables (true) or disables (false) an endpoint, like right-click > Disable in the Sound panel.
        public static int SetEnabled(string endpointId, bool enabled)
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            return pc.SetEndpointVisibility(endpointId, enabled ? 1 : 0);
        }

        /// Makes an endpoint the default for all three roles (console, multimedia, communications when asked).
        public static int SetDefault(string endpointId, bool alsoCommunications)
        {
            var pc = (IPolicyConfig)new PolicyConfigClient();
            int hr = pc.SetDefaultEndpoint(endpointId, 0);
            if (hr == 0) hr = pc.SetDefaultEndpoint(endpointId, 1);
            if (hr == 0 && alsoCommunications) hr = pc.SetDefaultEndpoint(endpointId, 2);
            return hr;
        }

        public static string DefaultId(bool render, int role)
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(render ? 0 : 1, role, out dev) != 0 || dev == null) return "";
            string id; dev.GetId(out id); return id;
        }
    }
}
