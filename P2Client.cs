/*
 * P2Client.cs - Internal HttpClient instance for FBPost.GetComments' 2nd pass.
 * Created on: 23:47 17-04-2022
 * Author: itsmevjnk
 */

using System.Net.Http;

using HRngBackend;

namespace HRngLite
{
    internal static class P2Client
    {
        /// <summary>
        ///  The HttpClient instance for FBPost.GetComments' 2nd pass. As such, this is not supposed to be logged in to Facebook.
        /// </summary>
        public static HttpClient Client = new HttpClient();

        /// <summary>
        ///  Constructor for this class. Prepares Client.
        /// </summary>
        static P2Client()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", UserAgent.Next());
            Client.Timeout = TimeSpan.FromSeconds(30); // Set timeout to 30 seconds
        }
    }
}