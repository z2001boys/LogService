using Microsoft.VisualStudio.TestTools.UnitTesting;
using LogService;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace LogService.Tests
{
	[TestClass()]
	public class LogHandleTests
	{
		[TestMethod()]
		public void LogHandleTest()
		{
			var log = new LogHandle();
			log.AddDebug("Block","Test");
			Thread.Sleep(1000);
			log.Dispose();
		}
	}
}