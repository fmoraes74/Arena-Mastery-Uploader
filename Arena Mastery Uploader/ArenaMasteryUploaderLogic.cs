using ArenaMasteryUploader.Data;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;
using Hearthstone_Deck_Tracker.Stats;
using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading.Tasks;
using System.Web;
using HtmlDocument = HtmlAgilityPack.HtmlDocument;

namespace ArenaMasteryUploader
{
    public class ArenaMasteryUploaderLogic
    {
        readonly string connectionError = "connection error. Try again, when www.arenamastery.com is available.";
        string username;
        SecureString password;

        Uri uriAddArena = new Uri("https://www.arenamastery.com/arena_gameupdate_ajax.php");
        Uri uriUpdateDate = new Uri("https://www.arenamastery.com/arena_update_ajax.php");

        public ArenaMasteryUploaderLogic(string username, SecureString password)
        {
            this.username = username;
            this.password = password;
        }

        public async Task<Result<UploadResults>> LoginAndSubmitArenaRuns(IEnumerable<Deck> runs, Action<double> setProgress = null)
        {
            System.Net.ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            Result<CookieContainer, UploadResults> result = await LogInToArenaMastery();
            CookieContainer cookieContainer = result.ResultData;
            if (result.Outcome == UploadResults.Success && cookieContainer != null)
            {
                Logger.WriteLine(string.Format("Uploading {0} run(s)", runs.Count()), ArenaMasteryUploaderPlugin.LogCategory);
                int i = 1;
                int max = runs.Count();
                foreach (Deck run in runs)
                {
                    Result<UploadResults> uploadResult = await SubmitArenaRun(run, cookieContainer);
                    if (uploadResult.Outcome != UploadResults.Success)
                    {
                        return uploadResult;
                    }
                    if (setProgress != null)
                        setProgress((double)i++ / max);
                }
                Logger.WriteLine("Upload successful", ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<UploadResults>(UploadResults.Success, string.Empty);
            }

            return new Result<UploadResults>(result.Outcome, result.ErrorMessage);
        }

        // returns null, if login failed
        private async Task<Result<CookieContainer, UploadResults>> LogInToArenaMastery()
        {
            CookieContainer cookieContainer = new CookieContainer();
            Uri uriLogin = new Uri("https://www.arenamastery.com/index.php");

            // get first page
            HttpWebRequest httpWebRequestIndex = WebRequest.Create(uriLogin) as HttpWebRequest;
            httpWebRequestIndex.ServicePoint.Expect100Continue = false;
            httpWebRequestIndex.Method = "HEAD";
            httpWebRequestIndex.ProtocolVersion = new Version("1.1");
            httpWebRequestIndex.KeepAlive = true;
            httpWebRequestIndex.CookieContainer = cookieContainer;

            HttpWebResponse webResponseIndex;

            try
            {
                webResponseIndex = await httpWebRequestIndex.GetResponseAsync() as HttpWebResponse;
            }
            catch (Exception e)
            {
                string connectionErrorExtended = connectionError + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace;
                Logger.WriteLine(connectionErrorExtended, ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<CookieContainer, UploadResults>(null, UploadResults.ConnectionError, connectionErrorExtended);
            }

            using (StreamReader sr = new StreamReader(webResponseIndex.GetResponseStream()))
            {
                string content = sr.ReadToEnd();
            }

            string body = CreatePostBodyString(GetLoginBodyArgs());

            Logger.WriteLine("Logging in as user " + username);
            
            // post login
            HttpWebRequest httpWebRequestLogin = WebRequest.Create(uriLogin) as HttpWebRequest;
            httpWebRequestLogin.ServicePoint.Expect100Continue = false;
            httpWebRequestLogin.Method = "POST";
            httpWebRequestLogin.ProtocolVersion = new Version("1.1");
            httpWebRequestLogin.KeepAlive = true;
            httpWebRequestLogin.ContentType = "application/x-www-form-urlencoded";
            httpWebRequestLogin.Referer = uriLogin.ToString();
            httpWebRequestLogin.CookieContainer = cookieContainer;
            using (StreamWriter stOut = new StreamWriter(httpWebRequestLogin.GetRequestStream(), System.Text.Encoding.ASCII))
            {
                stOut.Write(body);
                stOut.Close();
            }

            HttpWebResponse webResponseLogin;

            try
            {
                webResponseLogin = await httpWebRequestLogin.GetResponseAsync() as HttpWebResponse;
            }
            catch (Exception e)
            {
                string connectionErrorExtended = connectionError + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace;
                Logger.WriteLine(connectionErrorExtended, ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<CookieContainer, UploadResults>(null, UploadResults.ConnectionError, connectionErrorExtended);
            }

            using (StreamReader sr = new StreamReader(webResponseLogin.GetResponseStream()))
            {
                string content = sr.ReadToEnd();
            }

            bool success = webResponseLogin.ResponseUri.AbsoluteUri == @"https://www.arenamastery.com/player.php";

            if (!success)
            {
                Logger.WriteLine("Failed login due to uri " + webResponseLogin.ResponseUri.AbsoluteUri.ToString() + " " + webResponseLogin.StatusCode.ToString());
                if (webResponseLogin.ResponseUri.AbsoluteUri == uriLogin.ToString())
                {
                    Logger.WriteLine("Login to Arena Mastery failed: wrong credentials", ArenaMasteryUploaderPlugin.LogCategory);
                    return new Result<CookieContainer, UploadResults>(null, UploadResults.LoginFailedCredentialsWrong, "wrong credentials");
                }
                else
                {
                    Logger.WriteLine("Login to Arena Mastery failed: unknown error (Response url: " + webResponseLogin.ResponseUri.AbsoluteUri + ")", ArenaMasteryUploaderPlugin.LogCategory);
                    return new Result<CookieContainer, UploadResults>(null, UploadResults.LoginFailedUnknownError, "unknown error (Response url: " + webResponseLogin.ResponseUri.AbsoluteUri + ").");
                }
            }

            Logger.WriteLine("Login to Arena Mastery successful", ArenaMasteryUploaderPlugin.LogCategory);
            return new Result<CookieContainer, UploadResults>(cookieContainer, UploadResults.Success, string.Empty);
        }

        private async Task<Result<UploadResults>> SubmitArenaRun(Deck run, CookieContainer cookieContainer)
        {
            GameStats firstGame = run.DeckStats.Games.FirstOrDefault();
            ArenaMasteryClass deckClass;
            bool valid = Enum.TryParse(run.Class, out deckClass);
            
            Logger.WriteLine("Adding new arena for class " + deckClass.ToString());
            
            HttpWebRequest httpWebPostAddArena = WebRequest.Create("https://www.arenamastery.com/arena.php?new=" + ((int)deckClass).ToString()) as HttpWebRequest;
            httpWebPostAddArena.ServicePoint.Expect100Continue = false;
            httpWebPostAddArena.Method = "HEAD";
            httpWebPostAddArena.ProtocolVersion = new Version("1.1");
            httpWebPostAddArena.KeepAlive = true;
            httpWebPostAddArena.Referer = "https://www.arenamastery.com/player.php";
            httpWebPostAddArena.CookieContainer = cookieContainer;

            HttpWebResponse webResponseAddArena;
            try
            {
                webResponseAddArena = await httpWebPostAddArena.GetResponseAsync() as HttpWebResponse;
            }
            catch (Exception e)
            {
                string connectionErrorExtended = connectionError + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace;
                Logger.WriteLine(connectionErrorExtended, ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<UploadResults>(UploadResults.ConnectionError, connectionErrorExtended);
            }

            using (StreamReader sr = new StreamReader(webResponseAddArena.GetResponseStream()))
            {
                string content = sr.ReadToEnd();
            }
            
            string responseUri = webResponseAddArena.ResponseUri.AbsoluteUri.ToString();
            bool success = responseUri.StartsWith(@"https://www.arenamastery.com/arena.php?arena=");
            if (!success)
            {
                Logger.WriteLine("Submitting arena run failed"
                                 + Environment.NewLine + webResponseAddArena.StatusCode + " (" + webResponseAddArena.ResponseUri + ")", ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<UploadResults>(UploadResults.SubmittingArenaRunFailedUnknownError, "Failed to add new arena: " + webResponseAddArena.ResponseUri.AbsoluteUri.ToString());
            }

            string arenaId = responseUri.Substring(responseUri.LastIndexOf('=')+1);
            
            Logger.WriteLine("Successfully added arena " + arenaId);
                    
            int number = 1;
            string postReq = String.Empty;
            
            foreach(GameStats match in run.DeckStats.Games)
            {
                Logger.WriteLine("Uploading game #" + number);
                number++;
                // post add match request
                postReq = ConvertArenaMatchToRequest(match, arenaId);

                HttpWebRequest httpWebPostAddMatch = WebRequest.Create(uriAddArena) as HttpWebRequest;
                httpWebPostAddMatch.ServicePoint.Expect100Continue = false;
                httpWebPostAddMatch.Method = "POST";
                httpWebPostAddMatch.ProtocolVersion = new Version("1.1");
                httpWebPostAddMatch.KeepAlive = true;
                httpWebPostAddMatch.ContentType = "application/x-www-form-urlencoded";
                httpWebPostAddMatch.Referer = responseUri;
                httpWebPostAddMatch.CookieContainer = cookieContainer;
                using (StreamWriter stOut = new StreamWriter(httpWebPostAddMatch.GetRequestStream(), System.Text.Encoding.ASCII))
                {
                    stOut.Write(postReq);
                    stOut.Close();
                }

                HttpWebResponse webResponsePostAddMatch;
                try
                {
                    webResponsePostAddMatch = await httpWebPostAddMatch.GetResponseAsync() as HttpWebResponse;
                }
                catch (Exception e)
                {
                    string connectionErrorExtended = connectionError + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace;
                    Logger.WriteLine(connectionErrorExtended, ArenaMasteryUploaderPlugin.LogCategory);
                    return new Result<UploadResults>(UploadResults.ConnectionError, connectionErrorExtended);
                }

                using (StreamReader sr = new StreamReader(webResponsePostAddMatch.GetResponseStream()))
                {
                    string content = sr.ReadToEnd();
                }
                success = webResponsePostAddMatch.StatusCode == System.Net.HttpStatusCode.OK;
                if (!success)
                {
                    Logger.WriteLine("Submitting match failed"
                                     + Environment.NewLine + webResponsePostAddMatch.StatusCode + " (" + webResponsePostAddMatch.ResponseUri + ")", ArenaMasteryUploaderPlugin.LogCategory);
                    return new Result<UploadResults>(UploadResults.ConnectionError, "Unexpected uri: " + webResponsePostAddMatch.ResponseUri.AbsoluteUri.ToString());
                }
            }

            // post update rewards
            Logger.WriteLine("Posting rewards");
            postReq = ConvertArenaRewardsToRequest(run, arenaId);

            HttpWebRequest httpWebPostRewards = WebRequest.Create("https://www.arenamastery.com/arena_update_ajax.php") as HttpWebRequest;
            httpWebPostRewards.ServicePoint.Expect100Continue = false;
            httpWebPostRewards.Method = "POST";
            httpWebPostRewards.ProtocolVersion = new Version("1.1");
            httpWebPostRewards.KeepAlive = true;
            httpWebPostRewards.ContentType = "application/x-www-form-urlencoded";
            httpWebPostRewards.Referer = responseUri;
            httpWebPostRewards.CookieContainer = cookieContainer;
            using (StreamWriter stOut = new StreamWriter(httpWebPostRewards.GetRequestStream(), System.Text.Encoding.ASCII))
            {
                stOut.Write(postReq);
                stOut.Close();
            }

            HttpWebResponse webResponsePostRewards;
            try
            {
                webResponsePostRewards = await httpWebPostRewards.GetResponseAsync() as HttpWebResponse;
            }
            catch (Exception e)
            {
                string connectionErrorExtended = connectionError + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace;
                Logger.WriteLine(connectionErrorExtended, ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<UploadResults>(UploadResults.ConnectionError, connectionErrorExtended);
            }

            using (StreamReader sr = new StreamReader(webResponsePostRewards.GetResponseStream()))
            {
                string content = sr.ReadToEnd();
            }
            success = webResponsePostRewards.StatusCode == System.Net.HttpStatusCode.OK;
            if (!success)
            {
                Logger.WriteLine("Submitting rewards failed"
                                 + Environment.NewLine + webResponsePostRewards.StatusCode + " (" + webResponsePostRewards.ResponseUri + ")", ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<UploadResults>(UploadResults.ConnectionError, "Unexpected uri: " + webResponsePostRewards.ResponseUri.AbsoluteUri.ToString());
            }
            

            string date = firstGame != null ? firstGame.StartTime.ToString("MM/dd/yyyy", CultureInfo.GetCultureInfo("en-US")) : DateTime.Now.ToString("MM/dd/yyyy", CultureInfo.GetCultureInfo("en-US"));
            // post update date
            Logger.WriteLine("Setting arena date to " + date);

            postReq = ConvertArenaDate(arenaId, date);

            HttpWebRequest httpWebPostDate = WebRequest.Create(uriUpdateDate) as HttpWebRequest;
            httpWebPostDate.ServicePoint.Expect100Continue = false;
            httpWebPostDate.Method = "POST";
            httpWebPostDate.ProtocolVersion = new Version("1.1");
            httpWebPostDate.KeepAlive = true;
            httpWebPostDate.ContentType = "application/x-www-form-urlencoded";
            httpWebPostDate.Referer = responseUri;
            httpWebPostDate.CookieContainer = cookieContainer;
            using (StreamWriter stOut = new StreamWriter(httpWebPostDate.GetRequestStream(), System.Text.Encoding.ASCII))
            {
                stOut.Write(postReq);
                stOut.Close();
            }

            HttpWebResponse webResponsePostDate;
            try
            {
                webResponsePostDate = await httpWebPostDate.GetResponseAsync() as HttpWebResponse;
            }
            catch (Exception e)
            {
                string connectionErrorExtended = connectionError + Environment.NewLine + e.Message + Environment.NewLine + e.StackTrace;
                Logger.WriteLine(connectionErrorExtended, ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<UploadResults>(UploadResults.ConnectionError, connectionErrorExtended);
            }

            using (StreamReader sr = new StreamReader(webResponsePostDate.GetResponseStream()))
            {
                string content = sr.ReadToEnd();
            }
            success = webResponsePostDate.StatusCode == System.Net.HttpStatusCode.OK;
            if (!success)
            {
                Logger.WriteLine("Setting arena date failed"
                                 + Environment.NewLine + webResponsePostDate.StatusCode + " (" + webResponsePostDate.ResponseUri + ")", ArenaMasteryUploaderPlugin.LogCategory);
                return new Result<UploadResults>(UploadResults.ConnectionError, "Unexpected uri: " + webResponsePostDate.ResponseUri.AbsoluteUri.ToString());
            }

            PluginSettings.Instance.UploadedDecks.Add(run.DeckId);
            return new Result<UploadResults>(UploadResults.Success, string.Empty);
        }

        private Dictionary<string, string> GetLoginBodyArgs()
        {
            Dictionary<string, string> bodyArgs = new Dictionary<string, string>();
            bodyArgs["signin"] = "1";
            bodyArgs["playerEmail"] = username;
            bodyArgs["password"] = Encryption.ToInsecureString(password);
            return bodyArgs;
        }

        private string ConvertArenaMatchToRequest(GameStats match, string arena)
        {
            ArenaMasteryClass ArenaMasteryClass;
            bool success = Enum.TryParse<ArenaMasteryClass>(match.OpponentHero, out ArenaMasteryClass);
            Dictionary<string, string> bodyArgs = new Dictionary<string, string>()
            {
                { "action", "new" },
                { "opClassId", ((int) ArenaMasteryClass).ToString() },
                { "result", match.Result == GameResult.Win ? "1" : "0" },
                { "play", match.Coin ? "0" : "1" },
                { "arena", arena }
            };

            return CreatePostBodyString(bodyArgs);
        }
        
        private string ConvertArenaDate(string arena, string date)
        {
            Dictionary<string, string> bodyArgs = new Dictionary<string, string>()
            {
                { "action", "date" },
                { "arena", arena },
                { "newDate", date }
            };

            return CreatePostBodyString(bodyArgs);
        }
        
        private string ConvertArenaRewardsToRequest(Deck deck, string arena)
        {
            Dictionary<string, string> bodyArgs = new Dictionary<string, string>()
            {
                { "action", "rewards" },
                { "rewards", "1" },
                { "gold", deck.ArenaReward.Gold.ToString() },
                { "dust", deck.ArenaReward.Dust.ToString() },
                { "reg", deck.ArenaReward.Cards.Count(card => card != null && !card.Golden).ToString() },
                { "card", deck.ArenaReward.Cards.Count(card => card != null && card.Golden).ToString() },
                { "pack", deck.ArenaReward.Packs.Count(pack => pack != ArenaRewardPacks.None).ToString() },
                { "arena", arena }
            };

            return CreatePostBodyString(bodyArgs);
        }
        
        // create body for application/x-www-form-urlencoded
        private string CreatePostBodyString(Dictionary<string, string> bodyArgs)
        {
            string body = string.Empty;
            foreach (KeyValuePair<string, string> kvp in bodyArgs)
            {
                body += HttpUtility.UrlEncode(kvp.Key) + "=" + HttpUtility.UrlEncode(kvp.Value);
                body += "&";
            }
            if (body.Last() == '&')
                body = body.Remove(body.Length - 1);

            // return HttpUtility.UrlEncode(body);
            // return Uri.EscapeUriString(body);
            return body;
        }

    }
}
