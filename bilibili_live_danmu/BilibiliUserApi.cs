using LevelDB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace bilibi {
	public class UserApi
	{
		public UserApi() {
		}

		public async Task<UserInfo> GetUserInfo(UInt32 uin) {
			UserInfo info = GetUserInfoCache(uin);
			if (info != null)
            {
				return info;
            }
			Task<UserInfo> task;
			if (getUserInfoTaskMap.TryGetValue(uin, out task))
            {
				return await task;
            }
			task = GetUserInfoImpl(uin);
			getUserInfoTaskMap.TryAdd(uin, task);
			info = await task;
            getUserInfoTaskMap.TryRemove(uin, value: out _);
			if (info != null)
            {
				SetUserInfoCache(uin, info);
            }
			return info;
		}

		private UserInfo GetUserInfoCache(UInt32 uin)
        {
            try
            {
				var key = GetBytes(uin);
				byte[] res = levelDB.Get(key);
				if (res == null)
				{
					return null;
				}
				return JsonConvert.DeserializeObject<UserInfo>(Encoding.UTF8.GetString(res));
            } 
			catch (Exception)
            {
				return null;
            }
		}

		private void SetUserInfoCache(UInt32 uin, UserInfo info)
        {
			var value = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(info));
			var key = GetBytes(uin);
			levelDB.Put(key, value);
		}

		private async Task<UserInfo> GetUserInfoImpl(UInt32 uin)
        {
			try
            {
				HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.bilibili.com/x/space/acc/info?mid=" + uin.ToString());
				request.Method = "GET";
				request.ContentType = "text/html;charset=UTF-8";
				request.UserAgent = null;
				request.Timeout = 1000;
				HttpWebResponse response = await request.GetResponseAsync() as HttpWebResponse;
				StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.GetEncoding("UTF-8"));
				string result = reader.ReadToEnd();
				JObject respJ = JObject.Parse(result);
				JToken dataJ = respJ["data"];
				UserInfo userInfo = new UserInfo();
				JToken faceJ = dataJ["face"];
				userInfo.Face = (string)faceJ;
				return userInfo;
            } 
			catch (Exception)
            {
				return null;
            }
		}

		private byte[] GetBytes(UInt32 v)
        {
			byte[] res = new byte[4];
			res[0] = (byte)v;
			res[1] = (byte)((v >> 8) & 0xFF);
			res[2] = (byte)((v >> 16) & 0xFF);
			res[3] = (byte)((v >> 24) & 0xFF);
			return res;
        }

		private DB levelDB = new DB(new Options { CreateIfMissing = true }, "bilibili_user_info");

		private ConcurrentDictionary<UInt32, Task<UserInfo>> getUserInfoTaskMap = new ConcurrentDictionary<UInt32, Task<UserInfo>>();

		public static UserApi instance = new UserApi();
	}

	public class UserInfo
    {
		public string Face { set; get; }
    }
}
