/*
 * FBLogin.cs - Functions for logging into Facebook using credentials.
 * Created on: 00:00 17-04-2022
 * Author    : itsmevjnk
 */

using System.Net;
using System.Xml;

using HRngBackend;
using HtmlAgilityPack;

namespace HRngLite
{
    public static class FBLogin
    {
        /// <summary>
        ///  Verify that a Facebook account has been successfully logged in in CommonHTTP.Client.
        /// </summary>
        /// <returns><c>true</c> if there's a logged in account, or <c>false</c> otherwise.</returns>
        public static async Task<bool> VerifyLogin()
        {
            HtmlDocument doc = await HTTPHelper.GetRequest("https://mbasic.facebook.com");
            return (doc.DocumentNode.SelectSingleNode("//input[@name='login']") == null);
        }

        /// <summary>
        ///  Retrieve all cookies for mbasic.facebook.com.
        /// </summary>
        /// <param name="cookies">The string =&gt; string dictionary that will be used to store the resulting cookies.</param>
        private static void GetCookies(IDictionary<string, string>? cookies)
        {
            if (cookies != null)
            {
                var r_cookies = CommonHTTP.ClientHandler.CookieContainer.GetCookies(new Uri("https://mbasic.facebook.com"));
                foreach (Cookie cookie in r_cookies)
                {
                    if (!cookies.ContainsKey(cookie.Name)) cookies.Add(cookie.Name, cookie.Value);
                }
            }
        }

        /// <summary>
        ///  Log into a Facebook account for CommonHTTP.Client and optionally retrieve the resulting cookies.
        /// </summary>
        /// <param name="email">The email address or phone number associated with the account.</param>
        /// <param name="password">The password associated with the account.</param>
        /// <param name="cookies">The string =&gt; string dictionary that will be used to store the resulting cookies (optional).</param>
        /// <returns>
        ///  0 on success, or any of these failure results:
        ///  <list type="bullet">
        ///   <item><description>-1: Deformed login form</description></item>
        ///   <item><description>-2: Wrong credentials</description></item>
        ///   <item><description>
        ///    -3: Two-factor authentication required.<br/>
        ///    LoginOTP is supposed to be called to enter the OTP or wait for login approval from another device.
        ///   </description></item>
        ///   <item><description>-4: Deformed checkpoint page (account is locked?)</description></item>
        ///   <item><description>-5: Facebook was already logged in</description></item>
        ///  </list>
        /// </returns>
        public static async Task<int> Login(string email, string password, IDictionary<string, string>? cookies = null)
        {
            if (await VerifyLogin())
            {
                GetCookies(cookies);
                return -5;
            }

            /* Retrieve and process login page */
            HtmlDocument doc = await HTTPHelper.GetRequest("https://mbasic.facebook.com");
            var form = doc.DocumentNode.SelectSingleNode("//form[@id='login_form']");
            if (form == null) return -1;
            var login_data = new Dictionary<string, string>(); // What we will use to store our POST request data
            var inputs = form.SelectNodes(".//input[@name and (@type='hidden' or (@type='submit' and @name='login'))]");
            if (inputs == null) return -1;
            foreach (HtmlNode input in inputs) login_data.Add(input.Attributes["name"].DeEntitizeValue, ((input.Attributes["value"] == null) ? "" : input.Attributes["value"].DeEntitizeValue)); // This will add all the hidden data, including CSRF protection ones
            login_data.Add("email", email); login_data.Add("pass", password);
            
            /* Send login request */
            var resp = await CommonHTTP.Client.PostAsync(new Uri(new Uri("https://mbasic.facebook.com"), form.Attributes["action"].DeEntitizeValue).AbsoluteUri, new FormUrlEncodedContent(login_data)); // Send login request and retrieve result
            resp.EnsureSuccessStatusCode();
            string url = resp.RequestMessage.RequestUri.AbsoluteUri; // The resulting URL
            doc = new HtmlDocument(); doc.LoadHtml(await resp.Content.ReadAsStringAsync());

            /* Process login result */
            if (url.StartsWith("https://mbasic.facebook.com/checkpoint/") || url.StartsWith("https://mbasic.facebook.com/login/checkpoint/"))
            {
                /* Getting checkpointed, can either mean 2FA or locked account */
                return ((doc.DocumentNode.SelectSingleNode("//input[@id='approvals_code']") == null) ? -4 : -3); // If there's OTP field, it's 2FA
            }
            if (url.StartsWith("https://mbasic.facebook.com/login/save-device/"))
            {
                /* Save device notification, we'll just click OK (TODO: is this even needed?) */
                login_data.Clear(); // Reuse the login_data dictionary
                form = doc.DocumentNode.SelectSingleNode("//form");
                if (form != null)
                {
                    inputs = form.SelectNodes("./input");
                    if (inputs != null)
                    {
                        foreach (HtmlNode input in inputs) login_data.Add(input.Attributes["name"].DeEntitizeValue, ((input.Attributes["value"] == null) ? "" : input.Attributes["value"].DeEntitizeValue));
                    }
                    await HTTPHelper.PostRequest(new Uri(new Uri("https://mbasic.facebook.com"), form.Attributes["action"].DeEntitizeValue).AbsoluteUri, login_data);
                }
            }

            GetCookies(cookies);

            return 0;
        }

        /// <summary>
        ///  Perform two-factor authentication on the account being logged in in CommonHTTP.Client.
        /// </summary>
        /// <param name="otp">The time-based one time password provided by a TOTP generator such as Google Authenticator or Twilio Authy for the account.</param>
        /// <param name="cookies">The string =&gt; string dictionary that will be used to store the resulting cookies (optional).</param>
        /// <returns>
        ///  0 on success, or any of these failure results:
        ///  <list type="bullet">
        ///   <item><description>-1: Already logged in successfully (no 2FA required)</description></item>
        ///   <item><description>-2: Deformed checkpoint page</description></item>
        ///   <item><description>-4: Wrong OTP</description></item>
        ///   <item><description>-5: Login review required. The user is supposed to respond by logging into Facebook normally and approving/rejecting the login request. Since HRng rotates User-Agent strings, any unknown login attempts can be safely rejected.</description></item> 
        ///  </list>
        /// </returns>
        public static async Task<int> LoginOTP(string otp, IDictionary<string, string>? cookies = null)
        {
            if (await VerifyLogin()) return -1;

            /* Enter and submit OTP */
            HtmlDocument doc = await HTTPHelper.GetRequest("https://mbasic.facebook.com/checkpoint/");
            var form = doc.DocumentNode.SelectSingleNode("//form[contains(@action, '/login/checkpoint/')]");
            if (form == null) return -2;

            /* Save hidden POST data */
            var login_data = new Dictionary<string, string>();
            var inputs = form.SelectNodes("./input[@name]"); // Top level inputs
            if (inputs == null) return -2;
            foreach (HtmlNode input in inputs) login_data.Add(input.Attributes["name"].DeEntitizeValue, ((input.Attributes["value"] == null) ? "" : input.Attributes["value"].DeEntitizeValue));
            inputs = form.SelectNodes(".//article/section//input[@name and @type!='text']"); // Inputs in login code section
            if (inputs == null) return -2;
            foreach (HtmlNode input in inputs) login_data.Add(input.Attributes["name"].DeEntitizeValue, ((input.Attributes["value"] == null) ? "" : input.Attributes["value"].DeEntitizeValue));
            inputs = form.SelectNodes(".//article/div[1]//input[@name]"); // Inputs in submit button section
            if (inputs == null) return -2;
            foreach (HtmlNode input in inputs) login_data.Add(input.Attributes["name"].DeEntitizeValue, ((input.Attributes["value"] == null) ? "" : input.Attributes["value"].DeEntitizeValue));

            login_data.Add("approvals_code", otp); // OTP

            /* Send login request */
            var resp = await CommonHTTP.Client.PostAsync(new Uri(new Uri("https://mbasic.facebook.com"), form.Attributes["action"].DeEntitizeValue).AbsoluteUri, new FormUrlEncodedContent(login_data));
            resp.EnsureSuccessStatusCode();
            string url = resp.RequestMessage.RequestUri.AbsoluteUri; // The resulting URL
            doc = new HtmlDocument(); doc.LoadHtml(await resp.Content.ReadAsStringAsync());

            if (doc.DocumentNode.SelectSingleNode("//input[@id='approvals_code']") != null) return -4; // Wrong OTP

            if (url.StartsWith("https://mbasic.facebook.com/login/checkpoint/"))
            {
                /* Save device prompt, we have to click this or else we won't be logging in */
                form = doc.DocumentNode.SelectSingleNode("//form[contains(@action, '/login/checkpoint/')]");
                if (form == null) return -2;
                login_data.Clear();
                inputs = form.SelectNodes("./input[@name]"); // Top level inputs
                if (inputs == null) return -2;
                foreach (HtmlNode input in inputs) login_data.Add(input.Attributes["name"].DeEntitizeValue, ((input.Attributes["value"] == null) ? "" : input.Attributes["value"].DeEntitizeValue));
                inputs = form.SelectNodes(".//article/div[1]//input[@name]"); // Inputs in the div with Continue button
                if (inputs == null) return -2;
                foreach (HtmlNode input in inputs) login_data.Add(input.Attributes["name"].DeEntitizeValue, ((input.Attributes["value"] == null) ? "" : input.Attributes["value"].DeEntitizeValue));
                login_data.Add("name_action_selected", "save_device"); // Specify action
                doc = await HTTPHelper.PostRequest(new Uri(new Uri("https://mbasic.facebook.com"), form.Attributes["action"].DeEntitizeValue).AbsoluteUri, login_data); // Send prompt response
                if (doc.DocumentNode.SelectSingleNode("//*[@id='checkpointSubmitButton']") != null) return -5;
            }

            GetCookies(cookies);

            return 0;
        }

        /// <summary>
        ///  Get the user ID of the currently logged in user in CommonHTTP.Client.
        /// </summary>
        /// <returns>The logged in user's ID, or -1 if the operation fails.</returns>
        public static long GetUID()
        {
            var cookies = CommonHTTP.ClientHandler.CookieContainer.GetCookies(new Uri("https://mbasic.facebook.com"));
            foreach (Cookie cookie in cookies)
            {
                if (cookie.Name == "c_user") return Convert.ToInt64(cookie.Value);
            }
            return -1;
        }

        /// <summary>
        ///  Get the user ID of the currently logged in user in CommonHTTP.Client.
        /// </summary>
        /// <param name="cookies">A string =&gt; string dictionary of cookies for Facebook.</param>
        /// <returns>The logged in user's ID, or -1 if the operation fails.</returns>
        public static long GetUID(IDictionary<string, string> cookies)
        {
            if (cookies.ContainsKey("c_user")) return Convert.ToInt64(cookies["c_user"]);
            else return -1;
        }
    }
}
