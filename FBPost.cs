/*
 * FBPost.cs - Functions for gathering information on Facebook posts
 *             using only HttpClient.
 *             If any of HRng's main function fails, this file is
 *             the first place to check, as Facebook seems to change
 *             how their mobile (m.facebook.com) website work randomly,
 *             and it is the maintainer(s)' job to adapt this code
 *             to such changes.
 * Created on: 15:37 17-04-2022
 * Author    : itsmevjnk
 */

using System.Text.RegularExpressions;
using System.Web;

using HtmlAgilityPack;
using Newtonsoft.Json;

using HRngBackend;

namespace HRngLite
{
    public class FBPost : IFBPost
    {
        /* Properties specified by the IFBPost interface */
        public long PostID { get; internal set; } = -1;
        public long AuthorID { get; internal set; } = -1;
        public bool IsGroupPost { get; internal set; } = false;

        /// <summary>
        ///  Drop-in replacement for UID.Get() which adds handling code for cases where the account's URL is profile.php (which points to the account being checked).
        /// </summary>
        /// <returns>Same as <c>UID.Get()</c>.</returns>
        private async Task<long> GetUID(string url)
        {
            if (url.Contains("profile.php") && !url.Contains("id=")) return FBLogin.GetUID(); // The case we're looking for
            else return await UID.Get(url); // Attempt to get UID using UID.Get() as normal
        }

        /* Functions specified by the IFBPost interface */

        public async Task<int> Initialize(long id)
        {
            return await Initialize($"https://m.facebook.com/{id}");
        }

        public async Task<int> Initialize(string url)
        {
            if (url.Length == 0) return -1; // Return right away

            /* Change the domain to mbasic.facebook.com */
            if(!url.StartsWith("https://mbasic.facebook.com"))
            {
                url = Regex.Replace(url, "^.*://", ""); // Remove the schema (aka http(s)://) from the URL
                url = Regex.Replace(url, "^[^/]*", "https://mbasic.facebook.com"); // Perform the replacement
            }

            /* Request webpage to attempt to get post ID */
            HtmlDocument doc = new HtmlDocument();
            var resp = await CommonHTTP.Client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            doc.LoadHtml(await resp.Content.ReadAsStringAsync());
            var req_uri = resp.RequestMessage.RequestUri;
            if (req_uri.ToString().Contains("/watch/")) req_uri = new Uri(new Uri("https://mbasic.facebook.com"), doc.DocumentNode.SelectSingleNode("//footer//a[contains(@href,'/story.php')]").Attributes["href"].DeEntitizeValue); // Convert Facebook Watch link back to normal post link

            /* Detect group post */
            List<string> uri_segments = new List<string>();
            foreach (string seg in req_uri.Segments)
            {
                if (!seg.StartsWith('?') && seg != "/") uri_segments.Add(seg.Replace("/", ""));
            }
            if (uri_segments.Contains("groups")) IsGroupPost = true; // Group post detected

            /* Get author ID */
            AuthorID = -1; // Just in case this object was re-initialized with another post
            /* Attempt to get directly from URL */
            if (IsGroupPost && uri_segments[uri_segments.IndexOf("permalink") - 1].All(char.IsDigit)) AuthorID = Convert.ToInt64(uri_segments[uri_segments.IndexOf("permalink") - 1]);
            else
            {
                var url_params = HttpUtility.ParseQueryString(req_uri.Query);
                if (url_params.Get("id") != null) AuthorID = Convert.ToInt64(url_params.Get("id"));
            }
            /* Attempt to get from JSON-encoded data conveniently stored in <head>, should work with virtually everything except group posts */
            if (AuthorID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//script[contains(text(), 'SocialMediaPosting')]");
                if (elem != null)
                {
                    dynamic data_ft = JsonConvert.DeserializeObject(elem.InnerText);
                    if (data_ft != null && data_ft.ContainsKey("author") && data_ft.author.ContainsKey("identifier")) AuthorID = Convert.ToInt64(data_ft.author.identifier);
                }
            }
            /* Attempt to get from post container (not working with images) */
            if (AuthorID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//div[contains(@data-ft, 'content_owner_id_new')]");
                if (elem != null)
                {
                    dynamic data_ft = JsonConvert.DeserializeObject(elem.Attributes["data-ft"].DeEntitizeValue);
                    if (data_ft != null) AuthorID = Convert.ToInt64(data_ft.content_owner_id_new);
                }
            }
            /* Attempt to get from actor link (verified working with images) */
            if (AuthorID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//a[@class='actor-link']");
                if (elem != null && elem.Attributes["href"] != null) AuthorID = await GetUID(elem.Attributes["href"].DeEntitizeValue);

            }
            if (AuthorID < 0) return -2;

            /* Get post ID */
            PostID = -1;
            /* Attempt to get from actions div, which is by far the most reliable method */
            if (PostID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//div[starts-with(@id,'actions_')]");
                if (elem != null) PostID = Convert.ToInt64(elem.Attributes["id"].DeEntitizeValue.Replace("actions_", ""));
            }
            /* Attempt to get from JSON-encoded data conveniently stored in <head>, should work with virtually everything */
            if (PostID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//script[contains(text(), 'SocialMediaPosting')]");
                if (elem != null)
                {
                    dynamic data_ft = JsonConvert.DeserializeObject(elem.InnerText);
                    if (data_ft != null && data_ft.ContainsKey("identifier"))
                    {
                        if (((string)data_ft.identifier).Count(c => c == ';') == 3) PostID = Convert.ToInt64(((string)data_ft.identifier).Split(';')[1]); // [AuthorID];[PostID];;x format
                        else if (((string)data_ft.identifier).Count(c => c == ':') == 2) PostID = Convert.ToInt64(((string)data_ft.identifier).Split(':')[0]); // [PostID]:x:y format
                    }
                }
            }
            /* Attempt to get from post container (not working with images) */
            if (PostID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//div[contains(@data-ft, 'top_level_post_id')]");
                if (elem != null)
                {
                    dynamic data_ft = JsonConvert.DeserializeObject(elem.Attributes["data-ft"].Value);
                    if (data_ft != null) PostID = Convert.ToInt64(data_ft.top_level_post_id);
                }
            }
            /* Attempt to get from post/comment like link */
            if (PostID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//span[starts-with(@id, 'like_')]");
                if (elem != null) PostID = Convert.ToInt64(elem.Attributes["id"].DeEntitizeValue.Split('_')[1]);
            }
            /* Attempt to get from comment section */
            if (PostID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//div[starts-with(@id, 'ufi_')]");
                if (elem != null) PostID = Convert.ToInt64(elem.Attributes["id"].DeEntitizeValue.Replace("ufi_", ""));
            }
            if (PostID < 0) return -2;

            return 0;
        }

        /// <summary>
        ///  Helper function to extract data and save a comment.
        /// </summary>
        /// <param name="elem">The comment's root element (i.e. the one with data-sigil="comment" or data-sigil="comment inline-reply").</param>
        /// <param name="comments">The dictionary to store the comment in.</param>
        /// <param name="ids">The list of comment IDs fetched during last pass so such comments can be skipped in this pass.</param>
        /// <param name="reply">Set to -1 if the comment is not a reply, or the parent comment's ID.</param>
        /// <param name="muid">Whether to retrieve the UIDs of mentioned accounts.</param>
        /// <returns>The comment's ID.</returns>
        private async Task<long> SaveComment(HtmlNode elem, IDictionary<long, FBComment> comments, IList<long> ids, long reply, bool muid)
        {
            long id = Convert.ToInt64(elem.Attributes["id"].DeEntitizeValue); // Comment ID
            elem = elem.SelectSingleNode("./div"); // Enter the child div where everything is
            if (!ids.Contains(id))
            {
                if (!comments.ContainsKey(id))
                {
                    FBComment comment = new FBComment();
                    comment.ID = id;

                    /* Get author UID and name */
                    var elem_author_link = elem.SelectSingleNode("./h3/a");
                    if (elem_author_link.Attributes["href"] != null) comment.AuthorID = await UID.Get(elem_author_link.Attributes["href"].DeEntitizeValue);
                    comment.AuthorName = elem_author_link.InnerText;

                    /* Get comment text contents (including mentions) */
                    var elem_body = elem.SelectSingleNode("./div[1]");
                    comment.CommentText = HttpUtility.HtmlDecode(elem_body.InnerText);
                    comment.CommentText_HTML = elem_body.InnerHtml;
                    if (comment.CommentText != "")
                    {
                        int placeholder_cnt = -10;
                        var elem_mentions = elem_body.SelectNodes("./a");
                        if (elem_mentions != null)
                        {
                            foreach (var elem_mention in elem_mentions)
                            {
                                if (elem_mention.Attributes["href"] == null)
                                {
                                    comment.Mentions_Handle.Add($"{elem_mention.InnerText} ({placeholder_cnt})");
                                    if (muid) comment.Mentions_UID.Add(placeholder_cnt);
                                    placeholder_cnt--;
                                }
                                else
                                {
                                    string url = elem_mention.Attributes["href"].DeEntitizeValue;
                                    if (url.StartsWith("/") && !url.Contains(elem_mention.InnerText) && UID.GetHandle(url) != "")
                                    {
                                        comment.Mentions_Handle.Add(UID.GetHandle(url));
                                        if (muid) comment.Mentions_UID.Add(await GetUID(url));
                                    }
                                }
                            }
                        }
                    }

                    /* Get media/embed */
                    var elem_media = elem.SelectSingleNode("./div[2]");
                    if (elem_media.SelectSingleNode("./div/a[contains(@href, '/photo.php')]") != null) comment.ImageURL = new Uri(new Uri("https://mbasic.facebook.com"), elem_media.SelectSingleNode("./div/a[contains(@href, '/photo.php')]").Attributes["href"].DeEntitizeValue).AbsoluteUri; // Image
                    else if (elem_media.SelectSingleNode("./div/a[contains(@href, '/video_redirect/')]") != null) comment.VideoURL = new Uri(new Uri("https://mbasic.facebook.com"), elem_media.SelectSingleNode("./div/a[contains(@href, '/video_redirect/')]").Attributes["href"].DeEntitizeValue).AbsoluteUri; // Video
                    else if (elem_media.SelectSingleNode("./img") != null) comment.StickerURL = elem_media.SelectSingleNode("./img").Attributes["src"].DeEntitizeValue; // Sticker
                    else if (elem_media.SelectSingleNode("./a[contains(@href, 'lm.facebook.com')]") != null)
                    {
                        /* Embed */
                        var elem_embed = elem_media.SelectSingleNode("./a[contains(@href, 'lm.facebook.com')]");
                        comment.EmbedURL = elem_embed.Attributes["href"].DeEntitizeValue;
                        comment.EmbedTitle = elem_embed.SelectSingleNode(".//h3").InnerText;
                    }

                    comments.Add(id, comment);
                }
                FBComment cmt = comments[id];
                if (cmt.Parent == -1 && reply != -1) cmt.Parent = reply; // This comment is a reply
            }
            return id;
        }

        public async Task<Dictionary<long, FBComment>> GetComments(Func<float, bool>? cb = null, bool muid = true, bool p1 = true, bool p2 = false)
        {
            /* TODO: find a way to **RELIABLY** use m.facebook.com for enhanced speed */
            Dictionary<long, FBComment> comments = new Dictionary<long, FBComment>();

            int pass = (p1) ? 1 : 2; // We'll do pass 1 first
            float npass = ((p1) ? 1 : 0) + ((p2) ? 1 : 0); // Number of passes to do

            for (int pn = 0; pn < (int)npass; pn++, pass++) {
                int n = 0, total = 0;
                bool see_prev = false; // Set if there's only see previous and not see next
                string url = (IsGroupPost) ? $"https://mbasic.facebook.com/{PostID}" : $"https://mbasic.facebook.com/story.php?story_fbid={PostID}&id={AuthorID}";
                var reply_elems = new Dictionary<long, string>(); // List of view replies URLs
                var ids = comments.Keys; // List of comment IDs from previous pass (so we can skip comments that we've fetched)

                /* Load and parse all top-level comments */
                for (int page = 0; ; page++)
                {
                    HtmlDocument doc = await HTTPHelper.GetRequest((pass == 2) ? P2Client.Client : CommonHTTP.Client, url);
                    var ufi_comments = doc.DocumentNode.SelectSingleNode("//div[starts-with(@id,'ufi_')]").SelectSingleNode("./div/div[not(@id)]"); // The root element for everything that we will do below
                    if (ufi_comments == null) break;
                    var comment_elems = ufi_comments.SelectNodes("./div[not(starts-with(@id, 'see_'))]");
                    if (comment_elems.Count == 0) break;
                    total += comment_elems.Count;
                    if (cb != null & cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                    foreach (var elem in comment_elems)
                    {
                        long id = await SaveComment(elem, comments, ids.ToList(), -1, muid);

                        /* Find and save show replies link */
                        var reply_elem = elem.SelectSingleNode(".//div[contains(@id, 'comment_replies_')]/div/a[contains(@href, '/comment/replies/')]");
                        if (reply_elem != null) reply_elems.Add(id, new Uri(new Uri("https://mbasic.facebook.com"), reply_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri);

                        n++;
                        if (cb != null && cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                    }

                    /* Load next page */
                    var see_elem = ufi_comments.SelectSingleNode($"./div[starts-with(@id, 'see_{((see_prev) ? "prev" : "next")}')]/a");
                    if (see_elem == null) {
                        if (page == 0) {
                            see_prev = true;
                            see_elem = ufi_comments.SelectSingleNode($"./div[starts-with(@id, 'see_{((see_prev) ? "prev" : "next")}')]/a");
                            if (see_elem == null) break;
                        }
                        else break;
                    }
                    url = new Uri(new Uri("https://mbasic.facebook.com"), see_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri;
                }

                /* Load reply comments */
                foreach (KeyValuePair<long, string> replink in reply_elems)
                {
                    see_prev = false; // Reset so we can reuse
                    url = replink.Value;
                    long parent_id = replink.Key; // Just for convenience purposes
                    for (int page = 0; ; page++)
                    {
                        /* Retrieve page */
                        HtmlDocument doc = await HTTPHelper.GetRequest((pass == 2) ? P2Client.Client : CommonHTTP.Client, url);

                        /* Process replies */
                        var replies = doc.DocumentNode.SelectSingleNode($"//div[@id='{parent_id}']/following-sibling::div");
                        var elem_replies = replies.SelectNodes("./div[not(starts-with(@id, 'comment_replies'))]");
                        if (elem_replies.Count == 0) break;
                        total += elem_replies.Count;
                        if (cb != null & cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                        foreach (var elem in elem_replies)
                        {
                            await SaveComment(elem, comments, ids.ToList(), parent_id, muid);
                            n++;
                            if (cb != null && cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                        }
                        
                        /* Load next page if possible */
                        var see_elem = replies.SelectSingleNode($"./div[starts-with(@id, 'comment_replies_more_{((see_prev) ? 1 : 2)}')]/a");
                        if (see_elem == null)
                        {
                            if (page == 0)
                            {
                                see_prev = true;
                                see_elem = replies.SelectSingleNode($"./div[starts-with(@id, 'comment_replies_more_{((see_prev) ? 1 : 2)}')]/a");
                                if (see_elem == null) break;
                            }
                            else break;
                        }
                        url = new Uri(new Uri("https://mbasic.facebook.com"), see_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri;
                    }
                }
            }
            return comments;
        }

        /// <summary>
        ///  Helper function to parse Facebook's AJAX payload. This function more or less replaces Facebook's own JS-based parser that runs on the page if JavaScript is enabled.
        /// </summary>
        /// <param name="doc">The HTML document to operate on.</param>
        /// <param name="url">The AJAX fetch URL.</param>
        /// <returns>The deserialized AJAX payload.</returns>
        private async Task<dynamic> ParseAjax(HtmlDocument doc, string url)
        {
            string output = "";
            for (int i = 0; i < 5; i++)
            {
                var resp = await CommonHTTP.Client.GetAsync(url);
                resp.EnsureSuccessStatusCode();
                output = await resp.Content.ReadAsStringAsync();
                if (!output.StartsWith("for (;;);"))
                    Thread.Sleep(1000); // Wait for a while before trying again (possibly ratelimited)
                else break;
            }
            if (!output.StartsWith("for (;;);")) return null; // Cannot make AJAX request
            dynamic ajax = JsonConvert.DeserializeObject(output.Replace("for (;;);", "")); // Parse the AJAX payload, removing the infinite loop placed at the beginning to prevent it from being executed
            
            foreach (var action in ajax.payload.actions)
            {
                string cmd = action.cmd;
                /* Only append and replace are supported right now */
                if (cmd == "append")
                {
                    var target = doc.DocumentNode.SelectNodes($"//*[@id='{action.target}']");
                    if (target != null)
                    {
                        HtmlDocument doc2 = new HtmlDocument(); doc2.LoadHtml((string)action.html);
                        foreach (HtmlNode elem in target) elem.AppendChildren(doc2.DocumentNode.ChildNodes);
                    }
                }
                else if (cmd == "replace")
                {
                    var target = doc.DocumentNode.SelectNodes($"//*[@id='{action.target}']");
                    if (target != null)
                    {
                        HtmlDocument doc2 = new HtmlDocument(); doc2.LoadHtml((string)action.html);
                        foreach (HtmlNode elem in target) elem.ParentNode.ReplaceChild(doc2.DocumentNode.FirstChild, elem);
                    }
                }
            }

            return ajax;
        }

        public async Task<Dictionary<long, FBReact>> GetReactions(Func<float, bool>? cb = null)
        {
            Dictionary<long, FBReact> reactions = new Dictionary<long, FBReact>();

            /* Begin loading reactions */
            long total = -1;
            HtmlDocument doc;
            for (int i = 0; i < 5; i++)
            {
                doc = await HTTPHelper.GetRequest($"https://mbasic.facebook.com/ufi/reaction/profile/browser/?ft_ent_identifier={PostID}&av={FBLogin.GetUID()}"); // Fetch the total number of reactions. The av parameter is here for legitimacy purposes.
                HtmlNode total_elem = doc.DocumentNode.SelectSingleNode("//a[contains(@href, 'total_count')]");
                if (total_elem != null)
                {
                    total = Convert.ToInt64(HttpUtility.ParseQueryString((new Uri(new Uri("https://mbasic.facebook.com"), total_elem.Attributes["href"].DeEntitizeValue)).Query).Get("total_count"));
                    break;
                }
                else Thread.Sleep(500); // Wait for a while before trying again
            }
            if (total == -1) return null;
            
            doc = new HtmlDocument(); doc.LoadHtml("<div id='reaction_profile_browser'></div><div id='reaction_profile_pager'><a></a></div>"); // For housing data sent by Facebook's AJAX
            string url = $"https://m.facebook.com/ufi/reaction/profile/browser/fetch/?ft_ent_identifier={PostID}&limit=50&total_count={total}";
            dynamic ajax = await ParseAjax(doc, url);
            if (ajax == null) return null;

            /* As it turns out, Facebook conveniently provides us with a perfectly ordered list of shown users' IDs in the AJAX URL, so we can use that to speedrun the UID retrieval process */
            List<long> shown_users = new List<long>(); // Where we'll save the IDs
            string prev_shown = "---"; // Facebook stacks the new page's shown users before the previous pages' shown users, so we'll have to save the previous shown users list to filter out

            /* Another great thing about Facebook's AJAX is that it provides us with the UIDs of accounts with the Message button, so we can parse it and use that */
            var msg_users = new Dictionary<string, long>(); // Lookup table to convert element ID to UID

            /* Load all reactions */
            while (true)
            {
                /* Add element ID to msg_users */
                bool done = false;
                foreach (var action in ajax.payload.actions)
                {
                    if (action.cmd == "script")
                    {
                        /* Found what we're looking for */
                        dynamic script_json = JsonConvert.DeserializeObject(Regex.Replace((string)action.code, @"(^.*handle\()|(\);$)", "")); // Parse the JSON contained in the onload script

                        /* Load internal element name to element ID lookup table */
                        var int_lut = new Dictionary<string, string>();
                        foreach (var elem in script_json.elements) int_lut.Add((string)elem[0], (string)elem[1]);

                        /* Load instances */
                        foreach (var inst in script_json.instances)
                        {
                            if (inst[1].Count == 2 && inst[2].Count == 2 && ((string)inst[1][1]).StartsWith("__elem_") && int_lut.ContainsKey((string)inst[1][1]))
                            {
                                if (!msg_users.ContainsKey(int_lut[(string)inst[1][1]])) msg_users.Add(int_lut[(string)inst[1][1]], (long)inst[2][1]);
                            }
                        }
                    }
                    /* Check if we've ran out of reactions to load */
                    else if (action.cmd == "append" && action.target == "reaction_profile_browser" && action.html == "") done = true;
                }
                if (done) break;

                var next_elem = doc.DocumentNode.SelectSingleNode("//div[@id='reaction_profile_pager']/a");
                if (next_elem == null || next_elem.Attributes["data-ajaxify-href"] == null || next_elem.Attributes["href"] == null) break;

                string new_url = new Uri(new Uri("https://m.facebook.com"), next_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri;
                if (new_url == url) break; // No update
                url = new_url;

                string shown = HttpUtility.ParseQueryString(next_elem.Attributes["data-ajaxify-href"].DeEntitizeValue.Split('/').Last()).Get("shown_ids");
                foreach (string uid in shown.Replace(prev_shown, "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    shown_users.Add(Convert.ToInt64(uid));
                }
                prev_shown = "," + shown;
                ajax = await ParseAjax(doc, new_url);
                if (ajax == null) return null;
            }

            /* Go through each reaction */
            var react_elems = doc.DocumentNode.SelectNodes("//div[@id='reaction_profile_browser']/div");
            if (react_elems != null)
            {
                int n = 0;
                if (cb != null & cb(0) == false) return null;
                foreach (HtmlNode elem in react_elems)
                {
                    FBReact reaction = new FBReact();

                    /* Get the UID */
                    string link = elem.SelectSingleNode("./div[1]/div[1]//i[contains(@class, 'profpic')]/..").GetAttributeValue("href", "");
                    long uid = -1;
                    /* From shown user ID list */
                    if (n < shown_users.Count) uid = shown_users[n];
                    /* From element ID -> UID lookup table (above) */
                    if (uid == -1)
                    {
                        var id_elems = elem.SelectNodes(".//div[starts-with(@data-sigil, 'm-') and @id and @id != '']");
                        if (id_elems != null)
                        {
                            foreach (HtmlNode id_elem in id_elems)
                            {
                                string id = id_elem.Attributes["id"].DeEntitizeValue;
                                if (msg_users.ContainsKey(id))
                                {
                                    uid = msg_users[id];
                                    break;
                                }
                            }
                        }
                    }
                    /* From add friend button */
                    if (uid == -1)
                    {
                        var elem_data_store = elem.SelectSingleNode(".//a[contains(@data-store, 'id')]");
                        if (elem_data_store != null)
                        {
                            dynamic data_store = JsonConvert.DeserializeObject(elem_data_store.Attributes["data-store"].DeEntitizeValue);
                            uid = data_store.id;
                        }
                    }
                    /* From follow button */
                    if (uid == -1)
                    {
                        var elem_data_store = elem.SelectSingleNode(".//div[contains(@data-store, 'subject_id')]");
                        if (elem_data_store != null)
                        {
                            dynamic data_store = JsonConvert.DeserializeObject(elem_data_store.Attributes["data-store"].DeEntitizeValue);
                            uid = data_store.subject_id;
                        }
                    }
                    /* From page like button */
                    if (uid == -1)
                    {
                        var elem_data_store = elem.SelectSingleNode(".//div[contains(@data-store, 'pageID')]");
                        if (elem_data_store != null)
                        {
                            dynamic data_store = JsonConvert.DeserializeObject(elem_data_store.Attributes["data-store"].DeEntitizeValue);
                            uid = data_store.pageID;
                        }
                    }
                    /* Use UID lookup services */
                    if (uid == -1) uid = await GetUID(link);
                    else UID.Add(link, uid); // Contribute to the UID cache
                    reaction.UserID = uid;
                    reaction.UserName = elem.SelectSingleNode(".//strong").InnerText;

                    /* Get reaction type */
                    var elem_rtype = elem.SelectSingleNode("./i");
                    string r_class = elem_rtype.Attributes["class"].DeEntitizeValue;
                    if (r_class == "img _59aq img _2sxw")
                    {
                        /* Gather via style */
                        var lut = new Dictionary<string, ReactionEnum>
                        {
                            { @"background-image: url('https\3a //scontent.xx.fbcdn.net/m1/v/t6/An_UvxJXg9tdnLU3Y5qjPi0200MLilhzPXUgxzGjQzUMaNcmjdZA6anyrngvkdub33NZzZhd51fpCAEzNHFhko5aKRFP5fS1w_lKwYrzcNLupv27.png?ccb\3d 10-5\26 oh\3d 00_AT_UtuEDoMVLJKkcDbRHoNoa53G8J0mpfS_dwCh1wA6Low\26 oe\3d 6267A979\26 _nc_sid\3d 55e238');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Like },
                            { @"background-image: url('https\3a //scontent.xx.fbcdn.net/m1/v/t6/An-SJYN61eefFdoaV8pa0G_5_APCa0prZaqkZGXpCFeUCLCg89UPOqSkSZxJkLy0hAKKpUIPEB91mo9yBBfcAqcwmpEu5jN_jmJufFtJoVJCUklu.png?ccb\3d 10-5\26 oh\3d 00_AT9LWZUIqF4q_n_YfhRCbWD3MJw7UDAob8ro9qajiIC9Dg\26 oe\3d 6268FFDF\26 _nc_sid\3d 55e238');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Love },
                            { @"background-image: url('https\3a //scontent.xx.fbcdn.net/m1/v/t6/An_F9bJG7govfshSMBkvcRLcxT0jmiXVYKtr7lgH5AHgUrjjpZ1OD0xyxXYgf7arc0lWgCdrR_KN4Mg7RSN3Gm3W6Gg03N1tQ-ZXzVvFJ_KvvB4.png?ccb\3d 10-5\26 oh\3d 00_AT-QXPwVsdCmtpG-eaaCAPeJ0vyRpN7Hh-ka0bRFv3sG0w\26 oe\3d 62677BE7\26 _nc_sid\3d 55e238');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Haha },
                            { @"background-image: url('https\3a //scontent.xx.fbcdn.net/m1/v/t6/An_0KlxkBZwTJgSV9p2pDQkaZcuO9nFP4R72nyZmCnWKIxG_MSUbtZ_uBFHkKhQVvjgeou7ijfWCAKaRfSRFqQS9RcziMUL4BTtfpxJ2KfylUgpq.png?ccb\3d 10-5\26 oh\3d 00_AT8yFd2RuQdAYXwycpqv_FAv8Rqe0-OkxyglAuwbp8O9YQ\26 oe\3d 62691053\26 _nc_sid\3d 55e238');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Wow },
                            { @"background-image: url('https\3a //scontent.xx.fbcdn.net/m1/v/t6/An-9fyYLftTy_Mg2cJpugh-vEVNfbtI-fVn4FNS7K-sgIMu9pT62Tb1u9Dfm-xYLtjbLQk-yVHp_IlY_4iMVYp0xLpO7sJvbxbC2OIiRxzS02cOuKEoo.png?ccb\3d 10-5\26 oh\3d 00_AT-rlMaVYqENgmQ2opPaUpsWJZjjfASDpxINNh_Dv8CNDQ\26 oe\3d 62685A98\26 _nc_sid\3d 55e238');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Care },
                            { @"background-image: url('https\3a //scontent.xx.fbcdn.net/m1/v/t6/An-0mG6nK_Uk-eBw_Z5hXaQPl2Il-GAtgNisMF_CPi6qvu85Lx2-5PalMJvS7fIbuodHct0V3tJrvSxzau9mOcNxqVhoiy8lxxQ9edz-6r6_o9YroQ.png?ccb\3d 10-5\26 oh\3d 00_AT9C82ddR6wk3TQ-S-arHyG5c8DqFXFOUkfh8_-OOJDB-g\26 oe\3d 62685CC4\26 _nc_sid\3d 55e238');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Sad },
                            { @"background-image: url('https\3a //scontent.xx.fbcdn.net/m1/v/t6/An-OzaYGRs8HJMUUdL-Q9pzzUe-6dYQYH0YuulfJGzClIwZB6ubbGwhtChGS8FxnChgEmifrcrhalKyw7ubZeQmjvur00_4Bm3UKlJBnXJyqwKsR.png?ccb\3d 10-5\26 oh\3d 00_AT-uCaRV9XUxZU4OBjNHmpb8JHjCU5gLnm3N4bHiXtPBGg\26 oe\3d 62694A2B\26 _nc_sid\3d 55e238');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Angry },
                            { @"background-image: url('https\3a //static.xx.fbcdn.net/rsrc.php/v3/y4/r/W_Vdj9wA1g9.png');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Pride },
                            { @"background-image: url('https\3a //static.xx.fbcdn.net/rsrc.php/v3/yp/r/ZXxRcAexGpd.png');background-repeat:no-repeat;background-size:100% 100%;-webkit-background-size:100% 100%;width:16px;height:16px;", ReactionEnum.Thankful }
                        };
                        reaction.Reaction = lut[elem_rtype.Attributes["style"].DeEntitizeValue];
                    }
                    else
                    {
                        /* Gather via class */
                        var lut = new Dictionary<string, ReactionEnum>
                        {
                            { "_59aq img sp_LdwxfpG67Bn sx_3a00ef", ReactionEnum.Like },
                            { "_59aq img sp_LdwxfpG67Bn sx_f21116", ReactionEnum.Love },
                            { "_59aq img sp_LdwxfpG67Bn sx_ce3068", ReactionEnum.Haha },
                            { "_59aq img sp_LdwxfpG67Bn sx_d80e3a", ReactionEnum.Wow },
                            { "_59aq img sp_LdwxfpG67Bn sx_d8e63d", ReactionEnum.Care },
                            { "_59aq img sp_LdwxfpG67Bn sx_c3ed6c", ReactionEnum.Sad },
                            { "_59aq img sp_LdwxfpG67Bn sx_199220", ReactionEnum.Angry }
                        };
                        reaction.Reaction = lut[r_class];
                    }

                    /* Save reaction */
                    reactions.Remove(uid); // Remove previous reaction if it even exists
                    reactions.Add(uid, reaction);

                    n++;
                    if (cb != null && cb(100 * ((float)n / (float)react_elems.Count)) == false) return null;
                }
            }

            return reactions;
        }

        public async Task<Dictionary<long, string>> GetShares(Func<float, bool>? cb = null)
        {
            Dictionary<long, string> shares = new Dictionary<long, string>();

            /* Load shares page */
            HtmlDocument doc = await HTTPHelper.GetRequest($"https://mbasic.facebook.com/browse/shares?id={PostID}");

            /* Go through each account in each page */
            while (true)
            {
                var share_elems = doc.DocumentNode.SelectNodes("//h3/../div/div[not(@id)]/div");
                if (share_elems != null)
                {
                    int n = 0;
                    if (cb != null & cb(0) == false) return null;
                    foreach (HtmlNode elem in share_elems)
                    {
                        /* Get the UID */
                        string link = elem.SelectSingleNode(".//a[1]").Attributes["href"].DeEntitizeValue;
                        long uid = -1;

                        /* Get from add friend link */
                        var elem_add = elem.SelectSingleNode(".//a[contains(@href, 'add_friend.php')]");
                        if (elem_add != null)
                        {
                            uid = Convert.ToInt64(HttpUtility.ParseQueryString((new Uri(new Uri("https://mbasic.facebook.com"), elem_add.Attributes["href"].DeEntitizeValue)).Query).Get("id"));
                        }

                        /* TODO: Is follow link a thing? */

                        /* Use UID lookup services */
                        if (uid == -1) uid = await GetUID(link);
                        else UID.Add(link, uid); // Contribute to the UID cache

                        /* Save account */
                        if (!shares.ContainsKey(uid)) shares.Add(uid, elem.SelectSingleNode(".//a[1]").InnerText);

                        n++;
                        if (cb != null && cb(100 * ((float)n / (float)share_elems.Count)) == false) return null;
                    }
                }
                
                /* Load next page */
                var next_elem = doc.DocumentNode.SelectSingleNode("//div[@id='m_more_item']/a");
                if (next_elem == null) break;
                doc = await HTTPHelper.GetRequest(new Uri(new Uri("https://mbasic.facebook.com"), next_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri);
            }

            return shares;
        }
    }
}
