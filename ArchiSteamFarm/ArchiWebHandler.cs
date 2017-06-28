﻿/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
//using System.Web;// for HttpUtility;
//using System.Web.HttpUtility;
//using System.Net;// for HttpUtility;
//using System.Collections;
//using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using ArchiSteamFarm.JSON;
using ArchiSteamFarm.Localization;
using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;
using Formatting = Newtonsoft.Json.Formatting;

namespace ArchiSteamFarm {
	internal sealed class ArchiWebHandler : IDisposable {

		private const string IEconService = "IEconService";
		private const string IPlayerService = "IPlayerService";
		private const string ISteamUserAuth = "ISteamUserAuth";
		private const string ITwoFactorService = "ITwoFactorService";

		private const byte MinSessionTTL = GlobalConfig.DefaultConnectionTimeout / 4; // Assume session is valid for at least that amount of seconds

		// We must use HTTPS for SteamCommunity, as http would make certain POST requests failing (trades)
		private const string SteamCommunityHost = "steamcommunity.com";
		private const string SteamCommunityURL = "https://" + SteamCommunityHost;

		// We could (and should) use HTTPS for SteamStore, but that would make certain POST requests failing
		private const string SteamStoreHost = "store.steampowered.com";
		private const string SteamStoreURL = "http://" + SteamStoreHost;

		private static readonly SemaphoreSlim InventorySemaphore = new SemaphoreSlim(1);

		private static int Timeout = GlobalConfig.DefaultConnectionTimeout * 1000; // This must be int type

		private readonly Bot Bot;
		private readonly SemaphoreSlim PublicInventorySemaphore = new SemaphoreSlim(1);
		private readonly SemaphoreSlim SessionSemaphore = new SemaphoreSlim(1);
		private readonly SemaphoreSlim SteamApiKeySemaphore = new SemaphoreSlim(1);
		private readonly WebBrowser WebBrowser;

		private bool? CachedPublicInventory;
		private string CachedSteamApiKey;
		private DateTime LastSessionRefreshCheck = DateTime.MinValue;
		private ulong SteamID;

		internal ArchiWebHandler(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));
			WebBrowser = new WebBrowser(bot.ArchiLogger);
		}

		public void Dispose() {
			PublicInventorySemaphore.Dispose();
			SessionSemaphore.Dispose();
			SteamApiKeySemaphore.Dispose();
		}

		internal async Task<bool> AcceptTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string referer = SteamCommunityURL + "/tradeoffer/" + tradeID;
			string request = referer + "/accept";

			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "serverid", "1" },
				{ "tradeofferid", tradeID.ToString() }
			};

			return await WebBrowser.UrlPostRetry(request, data, referer).ConfigureAwait(false);
		}

      //public static NameValueCollection ParseQueryString (string query)
      //{
      //    return ParseQueryString (query, Encoding.UTF8);
      //}

      //public static NameValueCollection ParseQueryString (string query, Encoding encoding)
      //{
      //    if (query == null)
      //        throw new ArgumentNullException ("query");
      //    if (encoding == null)
      //        throw new ArgumentNullException ("encoding");
      //    if (query.Length == 0 || (query.Length == 1 && query[0] == '?'))
      //        return new HttpQSCollection ();
      //    if (query[0] == '?')
      //        query = query.Substring (1);
      //        
      //    NameValueCollection result = new HttpQSCollection ();
      //    ParseQueryString (query, encoding, result);
      //    return result;
      //}

      //internal static void ParseQueryString (string query, Encoding encoding, NameValueCollection result)
      //{
      //    if (query.Length == 0)
      //        return;

      //    string decoded = HtmlDecode (query);
      //    int decodedLength = decoded.Length;
      //    int namePos = 0;
      //    bool first = true;
      //    while (namePos <= decodedLength) {
      //        int valuePos = -1, valueEnd = -1;
      //        for (int q = namePos; q < decodedLength; q++) {
      //            if (valuePos == -1 && decoded [q] == '=') {
      //                valuePos = q + 1;
      //            } else if (decoded [q] == '&') {
      //                valueEnd = q;
      //                break;
      //            }
      //        }

      //        if (first) {
      //            first = false;
      //            if (decoded [namePos] == '?')
      //                namePos++;
      //        }
      //        
      //        string name, value;
      //        if (valuePos == -1) {
      //            name = null;
      //            valuePos = namePos;
      //        } else {
      //            name = UrlDecode (decoded.Substring (namePos, valuePos - namePos - 1), encoding);
      //        }
      //        if (valueEnd < 0) {
      //            namePos = -1;
      //            valueEnd = decoded.Length;
      //        } else {
      //            namePos = valueEnd + 1;
      //        }
      //        value = UrlDecode (decoded.Substring (valuePos, valueEnd - valuePos), encoding);

      //        result.Add (name, value);
      //        if (namePos == -1)
      //            break;
      //    }
      //}
        
        internal Dictionary<string, string> DecodeQueryParameters(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException("uri");

            if (uri.Query.Length == 0)
                return new Dictionary<string, string>();

            return uri.Query.TrimStart('?')
                            .Split(new[] { '&', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(parameter => parameter.Split(new[] { '=' }, StringSplitOptions.RemoveEmptyEntries))
                            .GroupBy(parts => parts[0],
                                     parts => parts.Length > 2 ? string.Join("=", parts, 1, parts.Length - 1) : (parts.Length > 1 ? parts[1] : ""))
                            .ToDictionary(grouping => grouping.Key,
                                          grouping => string.Join(",", grouping));
        }

		internal async Task<bool> BrowseURL(string URL) {
            if (URL == null) {
                return false;
            }

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string request = (URL.IndexOf("?") == -1)? URL: URL.Substring(0, URL.IndexOf("?"));
            Uri UnparsedUrl = new Uri(URL);
          //string query = UnparsedUrl.Query;
          //var data = HttpUtility.ParseQueryString(query);
            Dictionary<string, string> data = DecodeQueryParameters(UnparsedUrl);
            data.Add("sessionid", sessionID);
          //Dictionary<string, string> data = new Dictionary<string, string>(1) {
          //    { "sessionid", sessionID }
          //};

			HtmlDocument htmlDocument = await WebBrowser.UrlPostToHtmlDocumentRetry(request, data).ConfigureAwait(false);
			return true;
        }

		internal async Task<bool> BrowseURLGet(string URL) {
            if (URL == null) {
                return false;
            }

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string request = URL;

			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
			return true;
        }

		internal async Task<bool> AddFreeLicense(uint subID) {
			if (subID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(subID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			const string request = SteamStoreURL + "/checkout/addfreelicense";
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "subid", subID.ToString() },
				{ "action", "add_to_cart" }
			};

			HtmlDocument htmlDocument = await WebBrowser.UrlPostToHtmlDocumentRetry(request, data).ConfigureAwait(false);
			return htmlDocument?.DocumentNode.SelectSingleNode("//div[@class='add_free_content_success_area']") != null;
		}

		internal async Task<bool> ClearFromDiscoveryQueue(uint appID) {
			if (appID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(appID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamStoreURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string request = SteamStoreURL + "/app/" + appID;
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionid", sessionID },
				{ "appid_to_clear_from_queue", appID.ToString() }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		internal async Task DeclineTradeOffer(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return;
			}

			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(steamApiKey)) {
				return;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				await Task.Run(() => {
					using (dynamic iEconService = WebAPI.GetInterface(IEconService, steamApiKey)) {
						iEconService.Timeout = Timeout;

						try {
							response = iEconService.DeclineTradeOffer(
								tradeofferid: tradeID.ToString(),
								method: WebRequestMethods.Http.Post,
								secure: true
							);
						} catch (Exception e) {
							Bot.ArchiLogger.LogGenericWarningException(e);
						}
					}
				}).ConfigureAwait(false);
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
			}
		}

		internal async Task<HashSet<uint>> GenerateNewDiscoveryQueue() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamStoreURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return null;
			}

			const string request = SteamStoreURL + "/explore/generatenewdiscoveryqueue";
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionid", sessionID },
				{ "queuetype", "0" }
			};

			Steam.NewDiscoveryQueueResponse output = await WebBrowser.UrlPostToJsonResultRetry<Steam.NewDiscoveryQueueResponse>(request, data).ConfigureAwait(false);
			return output?.Queue;
		}

		internal async Task<HashSet<Steam.TradeOffer>> GetActiveTradeOffers() {
			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				await Task.Run(() => {
					using (dynamic iEconService = WebAPI.GetInterface(IEconService, steamApiKey)) {
						iEconService.Timeout = Timeout;

						try {
							response = iEconService.GetTradeOffers(
								active_only: 1,
								get_descriptions: 1,
								get_received_offers: 1,
								secure: true,
								time_historical_cutoff: uint.MaxValue
							);
						} catch (Exception e) {
							Bot.ArchiLogger.LogGenericWarningException(e);
						}
					}
				}).ConfigureAwait(false);
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
				return null;
			}

			Dictionary<ulong, (uint AppID, Steam.Item.EType Type)> descriptions = new Dictionary<ulong, (uint AppID, Steam.Item.EType Type)>();
			foreach (KeyValue description in response["descriptions"].Children) {
				ulong classID = description["classid"].AsUnsignedLong();
				if (classID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(classID));
					return null;
				}

				if (descriptions.ContainsKey(classID)) {
					continue;
				}

				uint appID = 0;

				string hashName = description["market_hash_name"].Value;
				if (!string.IsNullOrEmpty(hashName)) {
					appID = GetAppIDFromMarketHashName(hashName);
				}

				if (appID == 0) {
					appID = description["appid"].AsUnsignedInteger();
				}

				Steam.Item.EType type = Steam.Item.EType.Unknown;

				string descriptionType = description["type"].Value;
				if (!string.IsNullOrEmpty(descriptionType)) {
					type = GetItemType(descriptionType);
				}

				descriptions[classID] = (appID, type);
			}

			HashSet<Steam.TradeOffer> result = new HashSet<Steam.TradeOffer>();
			foreach (KeyValue trade in response["trade_offers_received"].Children) {
				Steam.TradeOffer.ETradeOfferState state = trade["trade_offer_state"].AsEnum<Steam.TradeOffer.ETradeOfferState>();
				if (state == Steam.TradeOffer.ETradeOfferState.Unknown) {
					Bot.ArchiLogger.LogNullError(nameof(state));
					return null;
				}

				if (state != Steam.TradeOffer.ETradeOfferState.Active) {
					continue;
				}

				ulong tradeOfferID = trade["tradeofferid"].AsUnsignedLong();
				if (tradeOfferID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(tradeOfferID));
					return null;
				}

				uint otherSteamID3 = trade["accountid_other"].AsUnsignedInteger();
				if (otherSteamID3 == 0) {
					Bot.ArchiLogger.LogNullError(nameof(otherSteamID3));
					return null;
				}

				Steam.TradeOffer tradeOffer = new Steam.TradeOffer(tradeOfferID, otherSteamID3, state);

				List<KeyValue> itemsToGive = trade["items_to_give"].Children;
				if (itemsToGive.Count > 0) {
					if (!ParseItems(descriptions, itemsToGive, tradeOffer.ItemsToGive)) {
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorParsingObject, nameof(itemsToGive)));
						return null;
					}
				}

				List<KeyValue> itemsToReceive = trade["items_to_receive"].Children;
				if (itemsToReceive.Count > 0) {
					if (!ParseItems(descriptions, itemsToReceive, tradeOffer.ItemsToReceive)) {
						Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorParsingObject, nameof(itemsToReceive)));
						return null;
					}
				}

				result.Add(tradeOffer);
			}

			return result;
		}

		internal async Task<HtmlDocument> GetBadgePage(byte page) {
			if (page == 0) {
				Bot.ArchiLogger.LogNullError(nameof(page));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/badges?l=english&p=" + page;
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<Steam.ConfirmationDetails> GetConfirmationDetails(string deviceID, string confirmationHash, uint time, MobileAuthenticator.Confirmation confirmation) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmation == null)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmation));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/details/" + confirmation.ID + "?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";

			Steam.ConfirmationDetails response = await WebBrowser.UrlGetToJsonResultRetry<Steam.ConfirmationDetails>(request).ConfigureAwait(false);
			if ((response == null) || !response.Success) {
				return null;
			}

			response.Confirmation = confirmation;
			return response;
		}

		internal async Task<HtmlDocument> GetConfirmations(string deviceID, string confirmationHash, uint time) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/conf?l=english&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<HtmlDocument> GetDiscoveryQueuePage() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/explore?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<HashSet<ulong>> GetFamilySharingSteamIDs() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/account/managedevices";
			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

			HtmlNodeCollection htmlNodes = htmlDocument?.DocumentNode.SelectNodes("(//table[@class='accountTable'])[last()]//a/@data-miniprofile");
			if (htmlNodes == null) {
				return null; // OK, no authorized steamIDs
			}

			HashSet<ulong> result = new HashSet<ulong>();

			foreach (string miniProfile in htmlNodes.Select(htmlNode => htmlNode.GetAttributeValue("data-miniprofile", null))) {
				if (string.IsNullOrEmpty(miniProfile)) {
					Bot.ArchiLogger.LogNullError(nameof(miniProfile));
					return null;
				}

				if (!uint.TryParse(miniProfile, out uint steamID3) || (steamID3 == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(steamID3));
					return null;
				}

				ulong steamID = new SteamID(steamID3, EUniverse.Public, EAccountType.Individual);
				result.Add(steamID);
			}

			return result;
		}

		internal async Task<HtmlDocument> GetGameCardsPage(ulong appID) {
			if (appID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(appID));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/my/gamecards/" + appID + "?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<Dictionary<uint, string>> GetMyOwnedGames() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/my/games/?xml=1";

			XmlDocument response = await WebBrowser.UrlGetToXMLRetry(request).ConfigureAwait(false);

			XmlNodeList xmlNodeList = response?.SelectNodes("gamesList/games/game");
			if ((xmlNodeList == null) || (xmlNodeList.Count == 0)) {
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(xmlNodeList.Count);
			foreach (XmlNode xmlNode in xmlNodeList) {
				XmlNode appNode = xmlNode.SelectSingleNode("appID");
				if (appNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(appNode));
					return null;
				}

				if (!uint.TryParse(appNode.InnerText, out uint appID)) {
					Bot.ArchiLogger.LogNullError(nameof(appID));
					return null;
				}

				XmlNode nameNode = xmlNode.SelectSingleNode("name");
				if (nameNode == null) {
					Bot.ArchiLogger.LogNullError(nameof(nameNode));
					return null;
				}

				result[appID] = nameNode.InnerText;
			}

			return result;
		}

		[SuppressMessage("ReSharper", "FunctionComplexityOverflow")]
		internal async Task<HashSet<Steam.Item>> GetMySteamInventory(bool tradable, HashSet<Steam.Item.EType> wantedTypes, HashSet<uint> wantedRealAppIDs = null) {
			if ((wantedTypes == null) || (wantedTypes.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(wantedTypes));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			HashSet<Steam.Item> result = new HashSet<Steam.Item>();

			string request = SteamCommunityURL + "/my/inventory/json/" + Steam.Item.SteamAppID + "/" + Steam.Item.SteamCommunityContextID + "?l=english&trading=" + (tradable ? "1" : "0") + "&start=";
			uint currentPage = 0;

			await InventorySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				while (true) {
					JObject jObject = await WebBrowser.UrlGetToJObjectRetry(request + currentPage).ConfigureAwait(false);

					IEnumerable<JToken> descriptions = jObject?.SelectTokens("$.rgDescriptions.*");
					if (descriptions == null) {
						return null; // OK, empty inventory
					}

					Dictionary<ulong, (uint AppID, Steam.Item.EType Type)> descriptionMap = new Dictionary<ulong, (uint AppID, Steam.Item.EType Type)>();
					foreach (JToken description in descriptions.Where(description => description != null)) {
						string classIDString = description["classid"]?.ToString();
						if (string.IsNullOrEmpty(classIDString)) {
							Bot.ArchiLogger.LogNullError(nameof(classIDString));
							continue;
						}

						if (!ulong.TryParse(classIDString, out ulong classID) || (classID == 0)) {
							Bot.ArchiLogger.LogNullError(nameof(classID));
							continue;
						}

						if (descriptionMap.ContainsKey(classID)) {
							continue;
						}

						uint appID = 0;

						string hashName = description["market_hash_name"]?.ToString();
						if (!string.IsNullOrEmpty(hashName)) {
							appID = GetAppIDFromMarketHashName(hashName);
						}

						if (appID == 0) {
							string appIDString = description["appid"]?.ToString();
							if (string.IsNullOrEmpty(appIDString)) {
								Bot.ArchiLogger.LogNullError(nameof(appIDString));
								continue;
							}

							if (!uint.TryParse(appIDString, out appID) || (appID == 0)) {
								Bot.ArchiLogger.LogNullError(nameof(appID));
								continue;
							}
						}

						Steam.Item.EType type = Steam.Item.EType.Unknown;

						string descriptionType = description["type"]?.ToString();
						if (!string.IsNullOrEmpty(descriptionType)) {
							type = GetItemType(descriptionType);
						}

						descriptionMap[classID] = (appID, type);
					}

					IEnumerable<JToken> items = jObject.SelectTokens("$.rgInventory.*");
					if (items == null) {
						Bot.ArchiLogger.LogNullError(nameof(items));
						return null;
					}

					foreach (JToken item in items.Where(item => item != null)) {
						Steam.Item steamItem;

						try {
							steamItem = item.ToObject<Steam.Item>();
						} catch (JsonException e) {
							Bot.ArchiLogger.LogGenericException(e);
							return null;
						}

						if (steamItem == null) {
							Bot.ArchiLogger.LogNullError(nameof(steamItem));
							return null;
						}

						steamItem.AppID = Steam.Item.SteamAppID;
						steamItem.ContextID = Steam.Item.SteamCommunityContextID;

						if (descriptionMap.TryGetValue(steamItem.ClassID, out (uint AppID, Steam.Item.EType Type) description)) {
							steamItem.RealAppID = description.AppID;
							steamItem.Type = description.Type;
						}

						if (!wantedTypes.Contains(steamItem.Type) || (wantedRealAppIDs?.Contains(steamItem.RealAppID) == false)) {
							continue;
						}

						result.Add(steamItem);
					}

					if (!bool.TryParse(jObject["more"]?.ToString(), out bool more) || !more) {
						break; // OK, last page
					}

					if (!uint.TryParse(jObject["more_start"]?.ToString(), out uint nextPage) || (nextPage <= currentPage)) {
						Bot.ArchiLogger.LogNullError(nameof(nextPage));
						return null;
					}

					currentPage = nextPage;
				}

				return result;
			} finally {
				Task.Run(async () => {
					await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
					InventorySemaphore.Release();
				}).Forget();
			}
		}

		internal async Task<Dictionary<uint, string>> GetOwnedGames(ulong steamID) {
			if (steamID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(steamID));
				return null;
			}

			string steamApiKey = await GetApiKey().ConfigureAwait(false);
			if (string.IsNullOrEmpty(steamApiKey)) {
				return null;
			}

			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				await Task.Run(() => {
					using (dynamic iPlayerService = WebAPI.GetInterface(IPlayerService, steamApiKey)) {
						iPlayerService.Timeout = Timeout;

						try {
							response = iPlayerService.GetOwnedGames(
								steamid: steamID,
								include_appinfo: 1,
								secure: true
							);
						} catch (Exception e) {
							Bot.ArchiLogger.LogGenericWarningException(e);
						}
					}
				}).ConfigureAwait(false);
			}

			if (response == null) {
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
				return null;
			}

			Dictionary<uint, string> result = new Dictionary<uint, string>(response["games"].Children.Count);
			foreach (KeyValue game in response["games"].Children) {
				uint appID = game["appid"].AsUnsignedInteger();
				if (appID == 0) {
					Bot.ArchiLogger.LogNullError(nameof(appID));
					return null;
				}

				result[appID] = game["name"].Value;
			}

			return result;
		}

		internal async Task<uint> GetServerTime() {
			KeyValue response = null;
			for (byte i = 0; (i < WebBrowser.MaxRetries) && (response == null); i++) {
				await Task.Run(() => {
					using (dynamic iTwoFactorService = WebAPI.GetInterface(ITwoFactorService)) {
						iTwoFactorService.Timeout = Timeout;

						try {
							response = iTwoFactorService.QueryTime(
								method: WebRequestMethods.Http.Post,
								secure: true
							);
						} catch (Exception e) {
							Bot.ArchiLogger.LogGenericWarningException(e);
						}
					}
				}).ConfigureAwait(false);
			}

			if (response != null) {
				return response["server_time"].AsUnsignedInteger();
			}

			Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxRetries));
			return 0;
		}

		internal async Task<HtmlDocument> GetSteamPrefPage() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			//const string request = SteamStoreURL + "/SteamAwards?l=english";
			const string request = SteamStoreURL + "/account/preferences/?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<HtmlDocument> GetSteamHomePage() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			//const string request = SteamStoreURL + "/SteamAwards?l=english";
			const string request = SteamCommunityURL + "/my/home?l=english";
			return await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);
		}

		internal async Task<byte?> GetTradeHoldDuration(ulong tradeID) {
			if (tradeID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(tradeID));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/tradeoffer/" + tradeID + "?l=english";

			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

			HtmlNode htmlNode = htmlDocument?.DocumentNode.SelectSingleNode("//div[@class='pagecontent']/script");
			if (htmlNode == null) { // Trade can be no longer valid
				return null;
			}

			string text = htmlNode.InnerText;
			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return null;
			}

			int index = text.IndexOf("g_daysTheirEscrow = ", StringComparison.Ordinal);
			if (index < 0) {
				Bot.ArchiLogger.LogNullError(nameof(index));
				return null;
			}

			index += 20;
			text = text.Substring(index);

			index = text.IndexOf(';');
			if (index < 0) {
				Bot.ArchiLogger.LogNullError(nameof(index));
				return null;
			}

			text = text.Substring(0, index);

			if (byte.TryParse(text, out byte holdDuration)) {
				return holdDuration;
			}

			Bot.ArchiLogger.LogNullError(nameof(holdDuration));
			return null;
		}

		internal async Task<bool?> HandleConfirmation(string deviceID, string confirmationHash, uint time, uint confirmationID, ulong confirmationKey, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmationID == 0) || (confirmationKey == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmationID) + " || " + nameof(confirmationKey));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			string request = SteamCommunityURL + "/mobileconf/ajaxop?op=" + (accept ? "allow" : "cancel") + "&p=" + deviceID + "&a=" + SteamID + "&k=" + WebUtility.UrlEncode(confirmationHash) + "&t=" + time + "&m=android&tag=conf&cid=" + confirmationID + "&ck=" + confirmationKey;

			Steam.ConfirmationResponse response = await WebBrowser.UrlGetToJsonResultRetry<Steam.ConfirmationResponse>(request).ConfigureAwait(false);
			return response?.Success;
		}

		internal async Task<bool?> HandleConfirmations(string deviceID, string confirmationHash, uint time, HashSet<MobileAuthenticator.Confirmation> confirmations, bool accept) {
			if (string.IsNullOrEmpty(deviceID) || string.IsNullOrEmpty(confirmationHash) || (time == 0) || (confirmations == null) || (confirmations.Count == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(deviceID) + " || " + nameof(confirmationHash) + " || " + nameof(time) + " || " + nameof(confirmations));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/mobileconf/multiajaxop";
			List<KeyValuePair<string, string>> data = new List<KeyValuePair<string, string>>(7 + confirmations.Count * 2) {
				new KeyValuePair<string, string>("op", accept ? "allow" : "cancel"),
				new KeyValuePair<string, string>("p", deviceID),
				new KeyValuePair<string, string>("a", SteamID.ToString()),
				new KeyValuePair<string, string>("k", confirmationHash),
				new KeyValuePair<string, string>("t", time.ToString()),
				new KeyValuePair<string, string>("m", "android"),
				new KeyValuePair<string, string>("tag", "conf")
			};

			foreach (MobileAuthenticator.Confirmation confirmation in confirmations) {
				data.Add(new KeyValuePair<string, string>("cid[]", confirmation.ID.ToString()));
				data.Add(new KeyValuePair<string, string>("ck[]", confirmation.Key.ToString()));
			}

			Steam.ConfirmationResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.ConfirmationResponse>(request, data).ConfigureAwait(false);
			return response?.Success;
		}

		internal async Task<bool> HasPublicInventory() {
			if (CachedPublicInventory.HasValue) {
				return CachedPublicInventory.Value;
			}

			// We didn't fetch state yet
			await PublicInventorySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (CachedPublicInventory.HasValue) {
					return CachedPublicInventory.Value;
				}

				bool? isInventoryPublic = await IsInventoryPublic().ConfigureAwait(false);
				if (!isInventoryPublic.HasValue) {
					return false;
				}

				CachedPublicInventory = isInventoryPublic.Value;
				return isInventoryPublic.Value;
			} finally {
				PublicInventorySemaphore.Release();
			}
		}

		internal async Task<bool> HasValidApiKey() => !string.IsNullOrEmpty(await GetApiKey().ConfigureAwait(false));

		internal static void Init() => Timeout = Program.GlobalConfig.ConnectionTimeout * 1000;

		internal async Task<bool> Init(ulong steamID, EUniverse universe, string webAPIUserNonce, string parentalPin) {
			if ((steamID == 0) || (universe == EUniverse.Invalid) || string.IsNullOrEmpty(webAPIUserNonce) || string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(steamID) + " || " + nameof(universe) + " || " + nameof(webAPIUserNonce) + " || " + nameof(parentalPin));
				return false;
			}

			string sessionID = Convert.ToBase64String(Encoding.UTF8.GetBytes(steamID.ToString()));

			// Generate an AES session key
			byte[] sessionKey = SteamKit2.CryptoHelper.GenerateRandomBlock(32);

			// RSA encrypt it with the public key for the universe we're on
			byte[] cryptedSessionKey;
			using (RSACrypto rsa = new RSACrypto(KeyDictionary.GetPublicKey(universe))) {
				cryptedSessionKey = rsa.Encrypt(sessionKey);
			}

			// Copy our login key
			byte[] loginKey = new byte[webAPIUserNonce.Length];
			Array.Copy(Encoding.ASCII.GetBytes(webAPIUserNonce), loginKey, webAPIUserNonce.Length);

			// AES encrypt the loginkey with our session key
			byte[] cryptedLoginKey = SteamKit2.CryptoHelper.SymmetricEncrypt(loginKey, sessionKey);

			// Do the magic
			Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.LoggingIn, ISteamUserAuth));

			KeyValue authResult = null;
			await Task.Run(() => {
				using (dynamic iSteamUserAuth = WebAPI.GetInterface(ISteamUserAuth)) {
					iSteamUserAuth.Timeout = Timeout;

					try {
						authResult = iSteamUserAuth.AuthenticateUser(
							steamid: steamID,
							sessionkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedSessionKey, 0, cryptedSessionKey.Length)),
							encrypted_loginkey: Encoding.ASCII.GetString(WebUtility.UrlEncodeToBytes(cryptedLoginKey, 0, cryptedLoginKey.Length)),
							method: WebRequestMethods.Http.Post,
							secure: true
						);
					} catch (Exception e) {
						Bot.ArchiLogger.LogGenericWarningException(e);
					}
				}
			}).ConfigureAwait(false);

			if (authResult == null) {
				return false;
			}

			string steamLogin = authResult["token"].Value;
			if (string.IsNullOrEmpty(steamLogin)) {
				Bot.ArchiLogger.LogNullError(nameof(steamLogin));
				return false;
			}

			string steamLoginSecure = authResult["tokensecure"].Value;
			if (string.IsNullOrEmpty(steamLoginSecure)) {
				Bot.ArchiLogger.LogNullError(nameof(steamLoginSecure));
				return false;
			}

			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("sessionid", sessionID, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLogin", steamLogin, "/", "." + SteamStoreHost));

			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamCommunityHost));
			WebBrowser.CookieContainer.Add(new Cookie("steamLoginSecure", steamLoginSecure, "/", "." + SteamStoreHost));

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);

			// Unlock Steam Parental if needed
			if (!parentalPin.Equals("0")) {
				if (!await UnlockParentalAccount(parentalPin).ConfigureAwait(false)) {
					return false;
				}
			}

			SteamID = steamID;
			LastSessionRefreshCheck = DateTime.UtcNow;
			return true;
		}

		internal async Task<bool> JoinGroup(ulong groupID) {
			if (groupID == 0) {
				Bot.ArchiLogger.LogNullError(nameof(groupID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			string request = SteamCommunityURL + "/gid/" + groupID;
			Dictionary<string, string> data = new Dictionary<string, string>(2) {
				{ "sessionID", sessionID },
				{ "action", "join" }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		internal async Task<bool> MarkInventory() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			const string request = SteamCommunityURL + "/my/inventory";

			await InventorySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				return await WebBrowser.UrlHeadRetry(request).ConfigureAwait(false);
			} finally {
				Task.Run(async () => {
					await Task.Delay(Program.GlobalConfig.InventoryLimiterDelay * 1000).ConfigureAwait(false);
					InventorySemaphore.Release();
				}).Forget();
			}
		}

		internal async Task<bool> MarkSentTrades() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			const string request = SteamCommunityURL + "/my/tradeoffers/sent";
			return await WebBrowser.UrlHeadRetry(request).ConfigureAwait(false);
		}

		internal void OnDisconnected() {
			CachedPublicInventory = null;
			CachedSteamApiKey = null;
			SteamID = 0;
		}

		internal async Task<(EResult Result, EPurchaseResultDetail? PurchaseResult)?> RedeemWalletKey(string key) {
			if (string.IsNullOrEmpty(key)) {
				Bot.ArchiLogger.LogNullError(nameof(key));
				return null;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamStoreURL + "/account/validatewalletcode";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "wallet_code", key }
			};

			Steam.RedeemWalletResponse response = await WebBrowser.UrlPostToJsonResultRetry<Steam.RedeemWalletResponse>(request, data).ConfigureAwait(false);
			if (response == null) {
				return null;
			}

			return (response.Result, response.PurchaseResultDetail);
		}

		internal async Task<bool> SendTradeOffer(HashSet<Steam.Item> inventory, ulong partnerID, string token = null) {
			if ((inventory == null) || (inventory.Count == 0) || (partnerID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(inventory) + " || " + nameof(inventory.Count) + " || " + nameof(partnerID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			Steam.TradeOfferRequest singleTrade = new Steam.TradeOfferRequest();
			HashSet<Steam.TradeOfferRequest> trades = new HashSet<Steam.TradeOfferRequest> { singleTrade };

			byte itemID = 0;
			foreach (Steam.Item item in inventory) {
				if (itemID >= Trading.MaxItemsPerTrade) {
					if (trades.Count >= Trading.MaxTradesPerAccount) {
						break;
					}

					singleTrade = new Steam.TradeOfferRequest();
					trades.Add(singleTrade);
					itemID = 0;
				}

				singleTrade.ItemsToGive.Assets.Add(item);
				itemID++;
			}

			const string referer = SteamCommunityURL + "/tradeoffer/new";
			const string request = referer + "/send";
			foreach (Dictionary<string, string> data in trades.Select(trade => new Dictionary<string, string>(6) {
				{ "sessionid", sessionID },
				{ "serverid", "1" },
				{ "partner", partnerID.ToString() },
				{ "tradeoffermessage", "Sent by ASF" },
				{ "json_tradeoffer", JsonConvert.SerializeObject(trade) },
				{ "trade_offer_create_params", string.IsNullOrEmpty(token) ? "" : new JObject { { "trade_offer_access_token", token } }.ToString(Formatting.None) }
			})) {
				if (!await WebBrowser.UrlPostRetry(request, data, referer).ConfigureAwait(false)) {
					return false;
				}
			}

			return true;
		}

		private async Task<string> GetApiKey() {
			if (CachedSteamApiKey != null) {
				// We fetched API key already, and either got valid one, or permanent AccessDenied
				// In any case, this is our final result
				return CachedSteamApiKey;
			}

			// We didn't fetch API key yet
			await SteamApiKeySemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (CachedSteamApiKey != null) {
					return CachedSteamApiKey;
				}

				(ESteamApiKeyState State, string Key)? result = await GetApiKeyState().ConfigureAwait(false);
				if (result == null) {
					// Request timed out, bad luck, we'll try again later
					return null;
				}

				switch (result.Value.State) {
					case ESteamApiKeyState.AccessDenied:
						// We succeeded in fetching API key, but it resulted in access denied
						// Cache the result as empty, API key is unavailable permanently
						CachedSteamApiKey = string.Empty;
						break;
					case ESteamApiKeyState.NotRegisteredYet:
						// We succeeded in fetching API key, and it resulted in no key registered yet
						// Let's try to register a new key
						if (!await RegisterApiKey().ConfigureAwait(false)) {
							// Request timed out, bad luck, we'll try again later
							return null;
						}

						// We should have the key ready, so let's fetch it again
						result = await GetApiKeyState().ConfigureAwait(false);
						if (result?.State != ESteamApiKeyState.Registered) {
							// Something went wrong, bad luck, we'll try again later
							return null;
						}

						goto case ESteamApiKeyState.Registered;
					case ESteamApiKeyState.Registered:
						// We succeeded in fetching API key, and it resulted in registered key
						// Cache the result, this is the API key we want
						CachedSteamApiKey = result.Value.Key;
						break;
					default:
						// We got an unhandled error, this should never happen
						Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.Value.State), result.Value.State));
						break;
				}

				return CachedSteamApiKey;
			} finally {
				SteamApiKeySemaphore.Release();
			}
		}

		private async Task<(ESteamApiKeyState State, string Key)?> GetApiKeyState() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/dev/apikey?l=english";
			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

			HtmlNode titleNode = htmlDocument?.DocumentNode.SelectSingleNode("//div[@id='mainContents']/h2");
			if (titleNode == null) {
				return null;
			}

			string title = titleNode.InnerText;
			if (string.IsNullOrEmpty(title)) {
				Bot.ArchiLogger.LogNullError(nameof(title));
				return (ESteamApiKeyState.Error, null);
			}

			if (title.Contains("Access Denied")) {
				return (ESteamApiKeyState.AccessDenied, null);
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@id='bodyContents_ex']/p");
			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));
				return (ESteamApiKeyState.Error, null);
			}

			string text = htmlNode.InnerText;
			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return (ESteamApiKeyState.Error, null);
			}

			if (text.Contains("Registering for a Steam Web API Key")) {
				return (ESteamApiKeyState.NotRegisteredYet, null);
			}

			int keyIndex = text.IndexOf("Key: ", StringComparison.Ordinal);
			if (keyIndex < 0) {
				Bot.ArchiLogger.LogNullError(nameof(keyIndex));
				return (ESteamApiKeyState.Error, null);
			}

			keyIndex += 5;

			if (text.Length <= keyIndex) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return (ESteamApiKeyState.Error, null);
			}

			text = text.Substring(keyIndex);
			if (text.Length != 32) {
				Bot.ArchiLogger.LogNullError(nameof(text));
				return (ESteamApiKeyState.Error, null);
			}

			if (Utilities.IsValidHexadecimalString(text)) {
				return (ESteamApiKeyState.Registered, text);
			}

			Bot.ArchiLogger.LogNullError(nameof(text));
			return (ESteamApiKeyState.Error, null);
		}

		internal async Task<bool> SteamAwardsVote(byte voteID, uint appID) {
			if ((voteID == 0) || (appID == 0)) {
				Bot.ArchiLogger.LogNullError(nameof(voteID) + " || " + nameof(appID));
				return false;
			}

			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamStoreURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			const string request = SteamStoreURL + "/salevote";
			Dictionary<string, string> data = new Dictionary<string, string>(3) {
				{ "sessionid", sessionID },
				{ "voteid", voteID.ToString() },
				{ "appid", appID.ToString() }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		private static uint GetAppIDFromMarketHashName(string hashName) {
			if (string.IsNullOrEmpty(hashName)) {
				ASF.ArchiLogger.LogNullError(nameof(hashName));
				return 0;
			}

			int index = hashName.IndexOf('-');
			if (index <= 0) {
				return 0;
			}

			return uint.TryParse(hashName.Substring(0, index), out uint appID) ? appID : 0;
		}

		private static Steam.Item.EType GetItemType(string name) {
			if (string.IsNullOrEmpty(name)) {
				ASF.ArchiLogger.LogNullError(nameof(name));
				return Steam.Item.EType.Unknown;
			}

			switch (name) {
				case "Booster Pack":
					return Steam.Item.EType.BoosterPack;
				case "Steam Gems":
					return Steam.Item.EType.SteamGems;
				default:
					if (name.EndsWith("Emoticon", StringComparison.Ordinal)) {
						return Steam.Item.EType.Emoticon;
					}

					if (name.EndsWith("Foil Trading Card", StringComparison.Ordinal)) {
						return Steam.Item.EType.FoilTradingCard;
					}

					if (name.EndsWith("Profile Background", StringComparison.Ordinal)) {
						return Steam.Item.EType.ProfileBackground;
					}

					return name.EndsWith("Trading Card", StringComparison.Ordinal) ? Steam.Item.EType.TradingCard : Steam.Item.EType.Unknown;
			}
		}

		private async Task<bool?> IsInventoryPublic() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return null;
			}

			const string request = SteamCommunityURL + "/my/edit/settings?l=english";
			HtmlDocument htmlDocument = await WebBrowser.UrlGetToHtmlDocumentRetry(request).ConfigureAwait(false);

			HtmlNode htmlNode = htmlDocument?.DocumentNode.SelectSingleNode("//input[@id='inventoryPrivacySetting_public']");
			if (htmlNode == null) {
				return null;
			}

			// Notice: checked doesn't have a value - null is lack of attribute, "" is attribute existing
			string state = htmlNode.GetAttributeValue("checked", null);

			return state != null;
		}

		private async Task<bool?> IsLoggedIn() {
			// It would make sense to use /my/profile here, but it dismisses notifications related to profile comments
			// So instead, we'll use some less intrusive link, such as /my/videos
			const string request = SteamCommunityURL + "/my/videos";

			Uri uri = await WebBrowser.UrlHeadToUriRetry(request).ConfigureAwait(false);
			return !uri?.AbsolutePath.StartsWith("/login", StringComparison.Ordinal);
		}

		private static bool ParseItems(Dictionary<ulong, (uint AppID, Steam.Item.EType Type)> descriptions, List<KeyValue> input, HashSet<Steam.Item> output) {
			if ((descriptions == null) || (input == null) || (input.Count == 0) || (output == null)) {
				ASF.ArchiLogger.LogNullError(nameof(descriptions) + " || " + nameof(input) + " || " + nameof(output));
				return false;
			}

			foreach (KeyValue item in input) {
				uint appID = item["appid"].AsUnsignedInteger();
				if (appID == 0) {
					ASF.ArchiLogger.LogNullError(nameof(appID));
					return false;
				}

				ulong contextID = item["contextid"].AsUnsignedLong();
				if (contextID == 0) {
					ASF.ArchiLogger.LogNullError(nameof(contextID));
					return false;
				}

				ulong classID = item["classid"].AsUnsignedLong();
				if (classID == 0) {
					ASF.ArchiLogger.LogNullError(nameof(classID));
					return false;
				}

				uint amount = item["amount"].AsUnsignedInteger();
				if (amount == 0) {
					ASF.ArchiLogger.LogNullError(nameof(amount));
					return false;
				}

				uint realAppID = appID;
				Steam.Item.EType type = Steam.Item.EType.Unknown;

				if (descriptions.TryGetValue(classID, out (uint AppID, Steam.Item.EType Type) description)) {
					realAppID = description.AppID;
					type = description.Type;
				}

				Steam.Item steamItem = new Steam.Item(appID, contextID, classID, amount, realAppID, type);
				output.Add(steamItem);
			}

			return true;
		}

		private async Task<bool> RefreshSessionIfNeeded() {
			if (SteamID == 0) {
				for (byte i = 0; (i < Program.GlobalConfig.ConnectionTimeout) && (SteamID == 0); i++) {
					await Task.Delay(1000).ConfigureAwait(false);
				}

				if (SteamID == 0) {
					return false;
				}
			}

			if (DateTime.UtcNow.Subtract(LastSessionRefreshCheck).TotalSeconds < MinSessionTTL) {
				return true;
			}

			await SessionSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (DateTime.UtcNow.Subtract(LastSessionRefreshCheck).TotalSeconds < MinSessionTTL) {
					return true;
				}

				bool? isLoggedIn = await IsLoggedIn().ConfigureAwait(false);
				if (isLoggedIn.GetValueOrDefault(true)) {
					LastSessionRefreshCheck = DateTime.UtcNow;
					return true;
				} else {
					Bot.ArchiLogger.LogGenericInfo(Strings.RefreshingOurSession);
					return await Bot.RefreshSession().ConfigureAwait(false);
				}
			} finally {
				SessionSemaphore.Release();
			}
		}

		private async Task<bool> RegisterApiKey() {
			if (!await RefreshSessionIfNeeded().ConfigureAwait(false)) {
				return false;
			}

			string sessionID = WebBrowser.CookieContainer.GetCookieValue(SteamCommunityURL, "sessionid");
			if (string.IsNullOrEmpty(sessionID)) {
				Bot.ArchiLogger.LogNullError(nameof(sessionID));
				return false;
			}

			const string request = SteamCommunityURL + "/dev/registerkey";
			Dictionary<string, string> data = new Dictionary<string, string>(4) {
				{ "domain", "localhost" },
				{ "agreeToTerms", "agreed" },
				{ "sessionid", sessionID },
				{ "Submit", "Register" }
			};

			return await WebBrowser.UrlPostRetry(request, data).ConfigureAwait(false);
		}

		private async Task<bool> UnlockParentalAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.UnlockingParentalAccount);

			if (!await UnlockParentalCommunityAccount(parentalPin).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				return false;
			}

			if (!await UnlockParentalStoreAccount(parentalPin).ConfigureAwait(false)) {
				Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);
				return false;
			}

			Bot.ArchiLogger.LogGenericInfo(Strings.Success);
			return true;
		}

		private async Task<bool> UnlockParentalCommunityAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			const string request = SteamCommunityURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "pin", parentalPin }
			};

			return await WebBrowser.UrlPostRetry(request, data, SteamCommunityURL).ConfigureAwait(false);
		}

		private async Task<bool> UnlockParentalStoreAccount(string parentalPin) {
			if (string.IsNullOrEmpty(parentalPin)) {
				Bot.ArchiLogger.LogNullError(nameof(parentalPin));
				return false;
			}

			const string request = SteamStoreURL + "/parental/ajaxunlock";
			Dictionary<string, string> data = new Dictionary<string, string>(1) {
				{ "pin", parentalPin }
			};

			return await WebBrowser.UrlPostRetry(request, data, SteamStoreURL).ConfigureAwait(false);
		}

		private enum ESteamApiKeyState : byte {
			Error,
			Registered,
			NotRegisteredYet,
			AccessDenied
		}
	}
}
