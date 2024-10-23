using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace LogService
{

	public class LogParameter
	{
		public const string TimeFormat = "yyyy-MM-dd HH:mm:ss.fff";
		public const int TimeSize = 24;
		public static string GetTime() => DateTime.Now.ToString(TimeFormat);

		public string SavePath { get; set; } = @"D:\Log";
		public int ParentId { get; set; } = -1;

		public string SemaphoreName { get; set; } = "S" + Guid.NewGuid().ToString("N");

		public string ShareMemoryName { get; set; } = "M" + Guid.NewGuid().ToString("N");

		public int RingBufferSize { get; set; } = 1024 * 1024 * 20;


		public override string ToString()
		{
			List<string> args = new List<string>();

			foreach (var property in typeof(LogParameter).GetProperties())
			{
				args.Add($"{property.Name}:{property.GetValue(this)}");
			}


			return string.Join(" ", args);
		}

		public static LogParameter Parse(string[] args)
		{
			LogParameter logParameter = new LogParameter();
			foreach (var arg in args)
			{
				var parts = arg.Split(':');
				if (parts.Length != 2)
				{
					continue;
				}
				//set by refelction
				var property = typeof(LogParameter).GetProperty(parts[0]);
				if (property != null)
				{
					//check need convert
					if (property.PropertyType == typeof(int))
					{
						property.SetValue(logParameter, int.Parse(parts[1]));
					}
					else
						property.SetValue(logParameter, parts[1]);
				}
			}
			return logParameter;
		}

	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct LogStruct
	{
		public int BlockLength;
		public int TimeLength;
		public int MessageLength;
	}

	public static class LogUtil
	{
		// 將 byte[] 轉換為 LogStruct
		public static T BytesToStruct<T>(byte[] arr) where T : struct
		{
			int size = Marshal.SizeOf<T>();
			IntPtr ptr = Marshal.AllocHGlobal(size);

			try
			{
				Marshal.Copy(arr, 0, ptr, size);
				return Marshal.PtrToStructure<T>(ptr);
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}
		}

		public static byte[] StructToBytes<T>(T structure) where T : struct
		{
			int size = Marshal.SizeOf(structure);
			byte[] arr = new byte[size];
			IntPtr ptr = Marshal.AllocHGlobal(size);

			try
			{
				Marshal.StructureToPtr(structure, ptr, true);
				Marshal.Copy(ptr, arr, 0, size);
			}
			finally
			{
				Marshal.FreeHGlobal(ptr);
			}

			return arr;
		}
	}
}
