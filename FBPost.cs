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

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;
using System.Linq;
using System.Xml;

using HtmlAgilityPack;
using Newtonsoft.Json;
using ExCSS;

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

            /* Detect group post */
            List<string> uri_segments = new List<string>();
            foreach (string seg in resp.RequestMessage.RequestUri.Segments)
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
                var url_params = HttpUtility.ParseQueryString(resp.RequestMessage.RequestUri.Query);
                if (url_params.Get("id") != null) AuthorID = Convert.ToInt64(url_params.Get("id"));
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
            /* Attempt to get from actor link (verified working with images) */
            if (AuthorID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//a[@class='actor-link']");
                if (elem != null && elem.Attributes["href"] != null) AuthorID = await GetUID(elem.Attributes["href"].DeEntitizeValue);

            }
            if (AuthorID < 0) return -2;

            /* Get post ID */
            PostID = -1;
            /* Attempt to get directly from URL */
            if (IsGroupPost && uri_segments[uri_segments.IndexOf("permalink") + 1].All(char.IsDigit)) AuthorID = Convert.ToInt64(uri_segments[uri_segments.IndexOf("permalink") + 1]);
            else
            {
                var url_params = HttpUtility.ParseQueryString(resp.RequestMessage.RequestUri.Query);
                if (url_params.Get("story_fbid") != null) PostID = Convert.ToInt64(url_params.Get("story_fbid"));
            }
            /* Attempt to get from JSON-encoded data conveniently stored in <head>, should work with virtually everything */
            if (PostID < 0)
            {
                var elem = doc.DocumentNode.SelectSingleNode("//script[contains(text(), 'SocialMediaPosting')]");
                if (elem != null)
                {
                    dynamic data_ft = JsonConvert.DeserializeObject(elem.InnerText);
                    if (data_ft != null && data_ft.ContainsKey("identifier")) PostID = Convert.ToInt64(((string)data_ft.author.identifier).Split(':')[0]);
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
        /// <param name="reply">Set to -1 if the comment is not a reply, 0 if the comment is a reply and the parent comment ID is to be discovered by the function, or the parent comment's ID.</param>
        /// <param name="muid">Whether to retrieve the UIDs of mentioned accounts.</param>
        private async Task SaveComment(HtmlNode elem, IDictionary<long, FBComment> comments, IList<long> ids, long reply, bool muid)
        {
            long id = Convert.ToInt64(elem.Attributes["id"].DeEntitizeValue); // Comment ID
            if (!ids.Contains(id))
            {
                if (!comments.ContainsKey(id))
                {
                    FBComment comment = new FBComment();
                    comment.ID = id;
                    var elem_profile_pict = elem.SelectSingleNode("./div[contains(@data-sigil, 'feed_story_ring')]");
                    comment.AuthorID = Convert.ToInt64(elem_profile_pict.Attributes["data-sigil"].DeEntitizeValue.Replace("feed_story_ring", ""));
                    var elem_comment = elem_profile_pict.SelectSingleNode("./following-sibling::div[1]");
                    var elem_author = elem_comment.SelectSingleNode(".//div[@class='_2b05']"); // TODO: Find a better way to do this (i.e. without using classes)
                    if (elem_author.SelectSingleNode("./a") != null && elem_author.SelectSingleNode("./a").Attributes["href"] != null) UID.Add(elem_author.SelectSingleNode("./a").Attributes["href"].DeEntitizeValue, comment.AuthorID);
                    comment.AuthorName = elem_author.InnerText; // TODO: Remove the Author text on top of the name
                    var elem_body = elem_comment.SelectSingleNode("./div[1]//div[@data-sigil='comment-body']");
                    if (elem_body != null)
                    {
                        comment.CommentText = HttpUtility.HtmlDecode(elem_body.InnerText);
                        comment.CommentText_HTML = elem_body.InnerHtml;
                    }
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
                    if (elem_comment.SelectNodes("./div").Count == 4)
                    {
                        /* Embedded content */
                        var elem_embed = elem_comment.SelectSingleNode("./div[2]");
                        var elem_bgimg = elem_embed.SelectSingleNode("./i[contains(@style, 'background-image')]");
                        if (elem_bgimg != null)
                        {
                            var parser = new StylesheetParser();
                            var style = parser.Parse(elem_bgimg.Attributes["style"].DeEntitizeValue).StyleRules.First() as StyleRule;
                            comment.StickerURL = style.Style.BackgroundImage.Replace("url(", "").Replace(")", "").Replace("\"", "").Replace("'", "");
                        }
                        var elem_embed2 = elem_embed;
                        if (!elem_embed2.Attributes.Contains("title")) elem_embed2 = elem_embed.SelectSingleNode("./div[@title]");
                        if (elem_embed2 != null && elem_embed2.Attributes.Contains("title"))
                        {
                            comment.EmbedTitle = elem_embed2.Attributes["title"].DeEntitizeValue;
                            comment.EmbedURL = elem_embed2.SelectSingleNode("./a").Attributes["href"].DeEntitizeValue;
                            if (comment.EmbedURL.StartsWith('/')) comment.EmbedURL = "https://m.facebook.com" + comment.EmbedURL;
                        }
                        var elem_attach = elem_embed.SelectSingleNode("./div[contains(@class, 'attachment')]/*");
                        if (elem_attach != null)
                        {
                            if (elem_attach.Name == "a" && elem_attach.Attributes.Contains("href") && (elem_attach.Attributes["href"].DeEntitizeValue.Contains("photo.php") || elem_attach.Attributes["href"].DeEntitizeValue.Contains("/photos/"))) comment.ImageURL = "https://m.facebook.com" + elem_attach.Attributes["href"].DeEntitizeValue;
                            if (elem_attach.Name == "div" && elem_attach.Attributes.Contains("data-store") && elem_attach.Attributes["data-store"].DeEntitizeValue.Contains("videoURL"))
                            {
                                dynamic data_store = JsonConvert.DeserializeObject(elem_attach.Attributes["data-store"].DeEntitizeValue);
                                if (data_store != null) comment.VideoURL = data_store.videoURL;
                            }
                        }
                    }
                    comments.Add(id, comment);
                }
                FBComment cmt = comments[id];
                if (cmt.Parent == -1 && reply != -1)
                {
                    /* This comment is a reply */
                    if (reply == 0)
                    {
                        /* Find parent comment */
                        var elem_parent = elem.SelectSingleNode("./ancestor::div[@data-sigil='comment']");
                        if (elem_parent != null) elem_parent = elem.SelectSingleNode("");
                        reply = Convert.ToInt64(elem_parent.Attributes["id"].DeEntitizeValue);
                    }
                    cmt.Parent = reply;
                }
            }
        }

        /// <summary>
        ///  Helper function to fetch a post page and parse it as HTML.
        /// </summary>
        /// <param name="url">The URL to fetch.</param>
        /// <param name="pass">The current GetComments pass. If pass=2, P2Client will be used; otherwise, CommonHTTP will be used.</param>
        /// <returns>The parsed HtmlDocument.</returns>
        private async Task<HtmlDocument> GetPage(string url, int pass)
        {
            HtmlDocument doc = new HtmlDocument();
            HttpResponseMessage resp;
            if (pass == 2) resp = await P2Client.Client.GetAsync(url);
            else resp = await CommonHTTP.Client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            doc.LoadHtml(Regex.Replace(await resp.Content.ReadAsStringAsync(), "(<div class=['\"]hidden_elem['\"]><code[^>]*><!--)|(--></code></div>)", "")); // Defeat Facebook's hide-elements-until-JS-load thing
            return doc;
        }

        public async Task<Dictionary<long, FBComment>> GetComments(Func<float, bool>? cb = null, bool muid = true, bool p1 = true, bool p2 = false)
        {
            Dictionary<long, FBComment> comments = new Dictionary<long, FBComment>();

            int pass = (p1) ? 1 : 2; // We'll do pass 1 first
            float npass = ((p1) ? 1 : 0) + ((p2) ? 1 : 0); // Number of passes to do

            for (int pn = 0; pn < (int)npass; pn++, pass++) {
                /* Load and parse all top-level and visible reply comments */
                int n = 0, total = 0;
                bool see_prev = false; // Set if there's only see previous and not see next
                string url = (IsGroupPost) ? $"https://m.facebook.com/{PostID}" : $"https://m.facebook.com/story.php?story_fbid={PostID}&id={AuthorID}";
                var reply_elems = new List<HtmlNode>(); // List of reply elements
                var ids = comments.Keys; // List of comment IDs from previous pass (so we can skip comments that we've fetched)
                for (int page = 0; ; page++)
                {
                    HtmlDocument doc = await GetPage(url, pass);
                    var comment_elems = doc.DocumentNode.SelectSingleNode("//div[@data-sigil='m-story-view']").SelectNodes(".//div[@data-sigil='comment' or @data-sigil='comment inline-reply']"); // Top-level comments only
                    if (comment_elems.Count == 0) break;
                    total += comment_elems.Count;
                    if (cb != null & cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                    foreach (var elem in comment_elems)
                    {
                        bool reply = (elem.Attributes["data-sigil"].DeEntitizeValue == "comment inline-reply");
                        await SaveComment(elem, comments, ids.ToList(), (reply) ? 0 : -1, muid);
                        n++;
                        if (cb != null && cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                    }

                    /* Process show previous/next replies links */
                    var elem_next = doc.DocumentNode.SelectNodes("//div[starts-with(@data-sigil,'replies-see-')]");
                    if (elem_next != null)
                    {
                        foreach (HtmlNode elem in elem_next) reply_elems.Add(elem.CloneNode(true));
                    }

                    /* Load next page */
                    var see_elem = doc.DocumentNode.SelectSingleNode($"//div[starts-with(@id, 'see_{((see_prev) ? "prev" : "next")}')]/a");
                    if (see_elem == null) {
                        if (page == 0) {
                            see_prev = true;
                            see_elem = doc.DocumentNode.SelectSingleNode($"//div[starts-with(@id, 'see_{((see_prev) ? "prev" : "next")}')]/a");
                            if (see_elem == null) break;
                        }
                        else break;
                    }
                    url = new Uri(new Uri("https://m.facebook.com"), see_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri;
                }

                /* Load hidden reply comments */
                foreach (HtmlNode replink in reply_elems)
                {
                    url = new Uri(new Uri("https://m.facebook.com"), replink.SelectSingleNode("./a").Attributes["href"].DeEntitizeValue).AbsoluteUri;
                    long parent = Convert.ToInt64(replink.Attributes["data-reply-to"].DeEntitizeValue);
                    bool direction = (replink.Attributes["data-sigil"].DeEntitizeValue == "replies-see-prev"); // Set if it's a see-prev (i.e. find other previous replies)
                    for (int page = 0; ; page++)
                    {
                        /* Retrieve page */
                        HtmlDocument doc = await GetPage(url, pass);
                        var elem_replies_parent = doc.DocumentNode.SelectSingleNode("//header/following-sibling::div[2]");
                        if (elem_replies_parent == null) break;

                        /* Process replies */
                        var elem_replies = elem_replies_parent.SelectNodes("./div[@data-sigil='comment']");
                        if (elem_replies.Count == 0) break;
                        total += elem_replies.Count;
                        if (cb != null & cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                        foreach (var elem in elem_replies)
                        {
                            await SaveComment(elem, comments, ids.ToList(), parent, muid);
                            n++;
                            if (cb != null && cb((100f / npass) * ((float)n / (float)total + pn)) == false) return null;
                        }
                        
                        /* Load next page if possible */
                        var see_elem = elem_replies_parent.SelectSingleNode($"./div[starts-with(@data-sigil, 'replies-see-{((direction) ? "prev" : "next")}')]/a");
                        if (see_elem == null) break;
                        url = new Uri(new Uri("https://m.facebook.com"), see_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri;
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
        private async Task ParseAjax(HtmlDocument doc, string url)
        {
            var resp = await CommonHTTP.Client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            dynamic ajax = JsonConvert.DeserializeObject((await resp.Content.ReadAsStringAsync()).Replace("for(;;);", "")); // Parse the AJAX payload, removing the infinite loop placed at the beginning to prevent it from being executed
            
            foreach (var action in ajax.payload.actions)
            {
                string cmd = action.cmd;
                /* Only append and replace are supported right now */
                if (cmd == "append")
                {
                    var target = doc.DocumentNode.SelectNodes($"//*[@id='{action.target}']");
                    if (target != null)
                    {
                        HtmlDocument doc2 = new HtmlDocument(); doc2.LoadHtml($"{action.html}");
                        foreach (HtmlNode elem in target) elem.AppendChildren(doc2.DocumentNode.ChildNodes);
                    }
                }
                else if (cmd == "replace")
                {
                    var target = doc.DocumentNode.SelectNodes($"//*[@id='{action.target}']");
                    if (target != null)
                    {
                        HtmlDocument doc2 = new HtmlDocument(); doc2.LoadHtml(action.html);
                        foreach (HtmlNode elem in target) elem.ParentNode.ReplaceChild(doc2.DocumentNode.FirstChild, elem);
                    }
                }
            }
        }

        public async Task<Dictionary<long, FBReact>> GetReactions(Func<float, bool>? cb = null)
        {
            var css_parser = new StylesheetParser(); // CSS parser

            Dictionary<long, FBReact> reactions = new Dictionary<long, FBReact>();

            /* Load reactions page */
            HtmlDocument doc = await HTTPHelper.GetRequest($"https://m.facebook.com/ufi/reaction/profile/browser/?ft_ent_identifier={PostID}");

            /* Get background-image + background-position => reaction type mapping */
            Dictionary<string, int> react_map = new Dictionary<string, int>(); // Having string as key instead of tuple should result in better performance
            foreach(HtmlNode elem in doc.DocumentNode.SelectNodes("//span[@data-sigil='reaction_profile_sigil' and not(contains(@data-store, 'all'))]"))
            {
                var style = css_parser.Parse(elem.SelectSingleNode(".//i").Attributes["style"].DeEntitizeValue).StyleRules.First() as StyleRule;
                dynamic data_store = JsonConvert.DeserializeObject(elem.Attributes["data-store"].DeEntitizeValue);
                react_map.Add($"{style.Style.BackgroundImage} {style.Style.BackgroundPosition}", Convert.ToInt32(data_store.reactionType));
            }

            /* As it turns out, Facebook conveniently provides us with a perfectly ordered list of shown users' IDs in the AJAX URL, so we can use that to speedrun the UID retrieval process */
            List<long> shown_users = new List<long>(); // Where we'll save the IDs
            string prev_shown = ""; // Facebook stacks the new page's shown users before the previous pages' shown users, so we'll have to save the previous shown users list to filter out
            /* Load all reactions */
            while (true)
            {
                var next_elem = doc.DocumentNode.SelectSingleNode("//div[@id='reaction_profile_pager']/a");
                if (next_elem == null || next_elem.Attributes["data-ajaxify-href"] == null || next_elem.Attributes["href"] == null) break;
                string shown = HttpUtility.ParseQueryString(next_elem.Attributes["data-ajaxify-href"].DeEntitizeValue.Split('/').Last()).Get("shown_ids");
                if (prev_shown.Length > 0) shown = shown.Replace(prev_shown, "");
                foreach (string uid in shown.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    shown_users.Add(Convert.ToInt64(uid));
                }
                prev_shown = "," + shown + ((prev_shown.Length > 0) ? "," : "") + prev_shown;
                await ParseAjax(doc, new Uri(new Uri("https://m.facebook.com"), next_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri);
            }

            /* Go through each reaction */
            var react_elems = doc.DocumentNode.SelectNodes("//div[@id='reaction_profile_browser']/div");
            if(react_elems != null)
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
                    /* Message button method is impossible to perform without JS and Selenium */
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
                    var style = css_parser.Parse(elem.SelectSingleNode("./i").Attributes["style"].DeEntitizeValue).StyleRules.First() as StyleRule;
                    reaction.Reaction = (ReactionEnum) react_map[$"{style.Style.BackgroundImage} {style.Style.BackgroundPosition}"];

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
            HtmlDocument doc = await HTTPHelper.GetRequest($"https://m.facebook.com/browse/shares?id={PostID}");

            /* Load all accounts */

            /* Go through each account */
            while (true)
            {
                var share_elems = doc.DocumentNode.SelectNodes("//div[contains(@data-sigil, 'content-pane')]//i[not(contains(@class, 'profpic'))]/..");
                if (share_elems != null)
                {
                    int n = 0;
                    if (cb != null & cb(0) == false) return null;
                    foreach (HtmlNode elem in share_elems)
                    {
                        /* Get the UID */
                        string link = elem.SelectSingleNode("./div[1]/div[1]//i[contains(@class, 'profpic')]/..").Attributes["href"].DeEntitizeValue;
                        long uid = -1;
                        /* These methods turn out to be working with shares too */
                        /* From add friend button */
                        var elem_data_store = elem.SelectSingleNode(".//a[contains(@data-store, 'id')]");
                        if (elem_data_store != null)
                        {
                            dynamic data_store = JsonConvert.DeserializeObject(elem_data_store.Attributes["data-store"].DeEntitizeValue);
                            uid = data_store.id;
                        }
                        /* From follow button */
                        if (uid == -1)
                        {
                            elem_data_store = elem.SelectSingleNode(".//div[contains(@data-store, 'subject_id')]");
                            if (elem_data_store != null)
                            {
                                dynamic data_store = JsonConvert.DeserializeObject(elem_data_store.Attributes["data-store"].DeEntitizeValue);
                                uid = data_store.subject_id;
                            }
                        }
                        /* From page like button */
                        if (uid == -1)
                        {
                            elem_data_store = elem.SelectSingleNode(".//div[contains(@data-store, 'pageID')]");
                            if (elem_data_store != null)
                            {
                                dynamic data_store = JsonConvert.DeserializeObject(elem_data_store.Attributes["data-store"].DeEntitizeValue);
                                uid = data_store.pageID;
                            }
                        }
                        /* Message button is not present so we can ignore it */
                        /* Use UID lookup services */
                        if (uid == -1) uid = await GetUID(link);
                        else UID.Add(link, uid); // Contribute to the UID cache

                        /* Save account */
                        if (!shares.ContainsKey(uid)) shares.Add(uid, elem.SelectSingleNode(".//strong").InnerText);

                        n++;
                        if (cb != null && cb(100 * ((float)n / (float)share_elems.Count)) == false) return null;
                    }
                }
                
                /* Load next page */
                var next_elem = doc.DocumentNode.SelectSingleNode("//div[@id='m_more_item']/a");
                if (next_elem == null) break;
                doc = await HTTPHelper.GetRequest(new Uri(new Uri("https://m.facebook.com"), next_elem.Attributes["href"].DeEntitizeValue).AbsoluteUri);
            }

            return shares;
        }
    }
}
