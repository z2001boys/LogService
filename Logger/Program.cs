using LogService;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Logger
{
	internal class Program
	{
		static MemoryMappedFile _memoryMapping = null;
		static Semaphore _logCounter;

		static void Main(string[] args)
		{
			var parameter = LogService.LogParameter.Parse(args);
			if (string.IsNullOrEmpty(parameter.SavePath))
			{
				parameter.SavePath = @"D:\Log";
				//create the folder
				if (!Directory.Exists(parameter.SavePath))
				{
					Directory.CreateDirectory(parameter.SavePath);
				}
			}

			//create the file
			_memoryMapping = MemoryMappedFile.CreateOrOpen(
				parameter.ShareMemoryName,
				parameter.RingBufferSize);
			//create the semphore
			_logCounter = new Semaphore(0, 1000, parameter.SemaphoreName);


			Process parentProcess = null;
			try
			{
				parentProcess = Process.GetProcessById(parameter.ParentId);
				WriteToFile(parameter.SavePath, $"Logger start with pid {parameter.ParentId}({parentProcess.ProcessName})");
			}
			catch
			{
				//do nothing
				WriteToFile(parameter.SavePath, $"No parent process-exit");
			}

			bool _disposing = false;
			//start a task to log data
			var writeTask = Task.Factory.StartNew(() =>
			{
				//create mmf view
				var headerSize = Marshal.SizeOf(typeof(LogService.LogStruct));
				int ReadPosition = 0;


				using (var mmfView = _memoryMapping.CreateViewAccessor(0, parameter.RingBufferSize))
				{
					while (true)
					{
						_logCounter.WaitOne();
						if (_disposing) break;

						//read header						
						var headerByte = ReadFromMmf(mmfView, parameter.RingBufferSize, ReadPosition, headerSize);
						var header = LogService.LogUtil.BytesToStruct<LogService.LogStruct>(headerByte);
						ReadPosition = (ReadPosition + headerSize) % parameter.RingBufferSize;

						//read block
						var blockByte = ReadFromMmf(mmfView, parameter.RingBufferSize, ReadPosition, header.BlockLength);
						var block = Encoding.Unicode.GetString(blockByte);
						ReadPosition = (ReadPosition + header.BlockLength) % parameter.RingBufferSize;

						//read time
						var timeByte = ReadFromMmf(mmfView, parameter.RingBufferSize, ReadPosition, header.TimeLength);
						var time = Encoding.Unicode.GetString(timeByte);
						ReadPosition = (ReadPosition + header.TimeLength) % parameter.RingBufferSize;

						//read message
						var messageByte = ReadFromMmf(mmfView, parameter.RingBufferSize, ReadPosition, header.MessageLength);
						var message = Encoding.Unicode.GetString(messageByte);
						ReadPosition = (ReadPosition + header.MessageLength) % parameter.RingBufferSize;

						WriteToFile(parameter.SavePath, block, time, message);
					}
				}

			});


			if (parentProcess != null)
			{
				Console.WriteLine("ready");
				parentProcess.WaitForExit();
			}


			WriteToFile(parameter.SavePath, $"Program stopped");
			_disposing = true;
			_logCounter.Release();
			writeTask.Wait();
			//release share memory
			_memoryMapping.Dispose();
			//release semaphore
			_logCounter.Dispose();
		}

		static void WriteToFile(string path, string block, string time, string message)
		{
			//curret date
			var date = DateTime.Now.ToString("yyyy_MM_dd");
			var folder = Path.Combine(path, date);
			Directory.CreateDirectory(folder);
			var fullFileName = Path.Combine(folder, block + ".txt");
			var timemessage = $"{time} : {message}";
			File.AppendAllText(fullFileName, timemessage + Environment.NewLine);
		}

		static void WriteToFile(string path, string message)
		{
			WriteToFile(path, "Log", LogParameter.GetTime(), message);
		}

		private static byte[] ReadFromMmf(MemoryMappedViewAccessor mmfView, long ringBufferSize, long readPosition, int dataSize)
		{
			byte[] buffer = new byte[dataSize];

			// 檢查讀取是否會跨越環形緩衝區的結尾
			if (readPosition + dataSize > ringBufferSize)
			{
				// 跨越環形緩衝區，需要將資料分兩部分讀取
				int firstPartSize = (int)(ringBufferSize - readPosition);
				int secondPartSize = dataSize - firstPartSize;

				// 第一部分讀取到環形緩衝區的結尾
				mmfView.ReadArray(readPosition, buffer, 0, firstPartSize);

				// 第二部分從環形緩衝區的起始位置讀取
				mmfView.ReadArray(0, buffer, firstPartSize, secondPartSize);
			}
			else
			{
				// 不會跨越環形緩衝區，直接讀取
				mmfView.ReadArray(readPosition, buffer, 0, dataSize);
			}

			return buffer;
		}

	}
}
