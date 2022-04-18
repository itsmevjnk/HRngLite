/*
 * P2Client.cs - Internal HttpClient instance for FBPost.GetComments' 2nd pass.
 * Created on: 23:47 17-04-2022
 * Author: itsmevjnk
 */

using System.Net.Http;

namespace HRngLite
{
    internal static class P2Client
    {
        /// <summary>
        ///  The HttpClient instance for FBPost.GetComments' 2nd pass. As such, this is not supposed to be logged in to Facebook.
        /// </summary>
        public static HttpClient Client = new HttpClient();
    }
}