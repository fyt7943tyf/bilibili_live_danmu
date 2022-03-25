using LevelDB;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace cache
{
	public class HttpCache
	{
		public HttpCache()
        {
		}

		public async Task<byte[]> Get(string key)
        {
			byte[] res = GetCache(key);
			if (res != null)
            {
				Console.WriteLine("CACHE HIT " + res.Length.ToString() + " " + key);
				return res;
            }
			Console.WriteLine("!CACHE HIT" + " " + key);
			Task<byte[]> task;
			if (getHttpDataTaskMap.TryGetValue(key, out task))
			{
				return await task;
			}
			task = GetImpl(key);
			getHttpDataTaskMap.TryAdd(key, task);
			res = await task;
			getHttpDataTaskMap.TryRemove(key, value: out _);
			if (res != null)
			{
				SetCache(key, res);
			}
			return res;
		}

		private byte[] GetCache(string key)
        {
			string res = levelDB.Get(key);
			if (res == null)
            {
				return null;
            }
			return Encoding.ASCII.GetBytes(res);
		}

		private void SetCache(string key, byte[] value)
		{
			var keyB = Encoding.UTF8.GetBytes(key);
			levelDB.Put(keyB, value);
		}

		private async Task<byte[]> GetImpl(string key)
        {
			try
            {
				HttpWebRequest request = (HttpWebRequest)WebRequest.Create(key);
				request.Method = "GET";
				request.Timeout = 1000;
				HttpWebResponse response = await request.GetResponseAsync() as HttpWebResponse;
				StreamReader reader = new StreamReader(response.GetResponseStream());
				string result = reader.ReadToEnd();
				return Encoding.ASCII.GetBytes(result);
			} 
			catch (Exception)
            {
				return null;
            }
		}

		private ConcurrentDictionary<string, Task<byte[]>> getHttpDataTaskMap = new ConcurrentDictionary<string, Task<byte[]>>();

		public static HttpCache instance = new HttpCache();
		private DB levelDB = new DB(new Options { CreateIfMissing = true }, "http_cache");
	}
}
