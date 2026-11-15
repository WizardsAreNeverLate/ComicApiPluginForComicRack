using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ComicApiPlugin.Plugin;

/// <summary>
/// A Windows job object that kills its processes when its last handle closes. This process holds the only handle, so the
/// service dies with ComicRack even when ComicRack crashes or is ended from Task Manager.
/// </summary>
internal sealed class ProcessJob : IDisposable
{
	private const int JobObjectExtendedLimitInformation = 9;
	private const uint KillOnJobClose = 0x2000;

	private readonly SafeFileHandle handle;

	public ProcessJob()
	{
		handle = CreateJobObject(IntPtr.Zero, null);
		if (handle.IsInvalid)
		{
			throw new Win32Exception();
		}
		var limits = new ExtendedLimitInformation { BasicLimitInformation = { LimitFlags = KillOnJobClose } };
		if (!SetInformationJobObject(handle, JobObjectExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimitInformation>()))
		{
			int error = Marshal.GetLastWin32Error();
			handle.Dispose();
			throw new Win32Exception(error);
		}
	}

	/// <summary>A process that has already exited cannot join (Windows answers access denied), and it has nothing left to kill.</summary>
	public void Add(Process process)
	{
		if (!AssignProcessToJobObject(handle, process.Handle))
		{
			int error = Marshal.GetLastWin32Error();
			if (!process.HasExited)
			{
				throw new Win32Exception(error);
			}
		}
	}

	public void Dispose() => handle.Dispose();

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimitInformation info, int length);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

	[StructLayout(LayoutKind.Sequential)]
	private struct BasicLimitInformation
	{
		public long PerProcessUserTimeLimit;
		public long PerJobUserTimeLimit;
		public uint LimitFlags;
		public UIntPtr MinimumWorkingSetSize;
		public UIntPtr MaximumWorkingSetSize;
		public uint ActiveProcessLimit;
		public UIntPtr Affinity;
		public uint PriorityClass;
		public uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IoCounters
	{
		public ulong ReadOperationCount;
		public ulong WriteOperationCount;
		public ulong OtherOperationCount;
		public ulong ReadTransferCount;
		public ulong WriteTransferCount;
		public ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ExtendedLimitInformation
	{
		public BasicLimitInformation BasicLimitInformation;
		public IoCounters IoInfo;
		public UIntPtr ProcessMemoryLimit;
		public UIntPtr JobMemoryLimit;
		public UIntPtr PeakProcessMemoryUsed;
		public UIntPtr PeakJobMemoryUsed;
	}
}
