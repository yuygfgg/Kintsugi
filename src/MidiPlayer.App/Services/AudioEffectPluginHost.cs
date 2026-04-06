using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MidiPlayer.App.Services;

internal sealed class AudioEffectPluginHost : IDisposable
{
    private const string LibraryName = "KintsugiPluginHost";
    private const int MessageBufferBytes = 4096;
    private nint _handle;
    private string _pluginName = string.Empty;

    public AudioEffectPluginHost()
    {
        _handle = NativeMethods.Create();
        if (_handle == 0)
        {
            throw new InvalidOperationException("Failed to initialize the native plug-in host.");
        }
    }

    public bool HasPlugin => _handle != 0 && NativeMethods.HasPlugin(_handle) != 0;

    public bool HasEditor => _handle != 0 && NativeMethods.HasEditor(_handle) != 0;

    public string PluginName => HasPlugin ? _pluginName : string.Empty;

    public void Load(string path, double sampleRate, int maximumBlockSize, int channels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ThrowIfDisposed();

        var messageBuffer = CreateMessageBuffer();
        if (NativeMethods.LoadPlugin(_handle, path, sampleRate, maximumBlockSize, channels, messageBuffer, messageBuffer.Length) == 0)
        {
            throw new InvalidOperationException(ReadMessageBuffer(messageBuffer));
        }

        _pluginName = GetPluginNameCore();
    }

    public void Prepare(double sampleRate, int maximumBlockSize, int channels)
    {
        ThrowIfDisposed();

        if (!HasPlugin)
        {
            return;
        }

        var messageBuffer = CreateMessageBuffer();
        if (NativeMethods.Prepare(_handle, sampleRate, maximumBlockSize, channels, messageBuffer, messageBuffer.Length) == 0)
        {
            throw new InvalidOperationException(ReadMessageBuffer(messageBuffer));
        }
    }

    public void Unload()
    {
        if (_handle == 0)
        {
            return;
        }

        NativeMethods.UnloadPlugin(_handle);
        _pluginName = string.Empty;
    }

    public void ShowEditor()
    {
        ThrowIfDisposed();

        if (!HasPlugin)
        {
            throw new InvalidOperationException("Load a plug-in first.");
        }

        var messageBuffer = CreateMessageBuffer();
        if (NativeMethods.ShowEditor(_handle, messageBuffer, messageBuffer.Length) == 0)
        {
            throw new InvalidOperationException(ReadMessageBuffer(messageBuffer));
        }
    }

    public byte[] GetState()
    {
        ThrowIfDisposed();

        if (!HasPlugin)
        {
            throw new InvalidOperationException("Load a plug-in first.");
        }

        int stateSize = NativeMethods.GetStateSize(_handle);
        if (stateSize <= 0)
        {
            return [];
        }

        var stateBuffer = new byte[stateSize];
        var messageBuffer = CreateMessageBuffer();
        if (NativeMethods.GetState(_handle, stateBuffer, stateBuffer.Length, messageBuffer, messageBuffer.Length) == 0)
        {
            throw new InvalidOperationException(ReadMessageBuffer(messageBuffer));
        }

        return stateBuffer;
    }

    public void SetState(byte[] state)
    {
        ArgumentNullException.ThrowIfNull(state);
        ThrowIfDisposed();

        if (!HasPlugin)
        {
            throw new InvalidOperationException("Load a plug-in first.");
        }

        if (state.Length == 0)
        {
            return;
        }

        var messageBuffer = CreateMessageBuffer();
        if (NativeMethods.SetState(_handle, state, state.Length, messageBuffer, messageBuffer.Length) == 0)
        {
            throw new InvalidOperationException(ReadMessageBuffer(messageBuffer));
        }

        _pluginName = GetPluginNameCore();
    }

    public void ProcessInterleaved(IntPtr interleavedFloatBuffer, int frames, int channels)
    {
        if (_handle == 0 || !HasPlugin || interleavedFloatBuffer == IntPtr.Zero || frames <= 0 || channels <= 0)
        {
            return;
        }

        NativeMethods.ProcessInterleaved(_handle, interleavedFloatBuffer, frames, channels);
    }

    public void Dispose()
    {
        if (_handle == 0)
        {
            return;
        }

        NativeMethods.Destroy(_handle);
        _handle = 0;
        _pluginName = string.Empty;
    }

    private string GetPluginNameCore()
    {
        var messageBuffer = CreateMessageBuffer();
        if (NativeMethods.GetPluginName(_handle, messageBuffer, messageBuffer.Length) == 0)
        {
            return string.Empty;
        }

        return ReadMessageBuffer(messageBuffer);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
    }

    private static byte[] CreateMessageBuffer()
        => new byte[MessageBufferBytes];

    private static string ReadMessageBuffer(byte[] buffer)
    {
        int zeroIndex = Array.IndexOf(buffer, (byte)0);
        int length = zeroIndex >= 0 ? zeroIndex : buffer.Length;
        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    private static class NativeMethods
    {
        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_Create")]
        public static extern nint Create();

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_Destroy")]
        public static extern void Destroy(nint host);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_LoadPlugin")]
        public static extern int LoadPlugin(
            nint host,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            double sampleRate,
            int maximumBlockSize,
            int channels,
            byte[] errorBuffer,
            int errorBufferBytes);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_Prepare")]
        public static extern int Prepare(
            nint host,
            double sampleRate,
            int maximumBlockSize,
            int channels,
            byte[] errorBuffer,
            int errorBufferBytes);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_UnloadPlugin")]
        public static extern void UnloadPlugin(nint host);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_HasPlugin")]
        public static extern int HasPlugin(nint host);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_HasEditor")]
        public static extern int HasEditor(nint host);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_ShowEditor")]
        public static extern int ShowEditor(nint host, byte[] errorBuffer, int errorBufferBytes);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_ProcessInterleaved")]
        public static extern void ProcessInterleaved(nint host, IntPtr interleavedBuffer, int frames, int channels);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_GetPluginName")]
        public static extern int GetPluginName(nint host, byte[] nameBuffer, int nameBufferBytes);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_GetStateSize")]
        public static extern int GetStateSize(nint host);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_GetState")]
        public static extern int GetState(
            nint host,
            byte[] stateBuffer,
            int stateBufferBytes,
            byte[] errorBuffer,
            int errorBufferBytes);

        [DllImport(LibraryName, EntryPoint = "KintsugiPluginHost_SetState")]
        public static extern int SetState(
            nint host,
            byte[] stateBuffer,
            int stateBufferBytes,
            byte[] errorBuffer,
            int errorBufferBytes);
    }
}
