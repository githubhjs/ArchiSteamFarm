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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using HtmlAgilityPack;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.JSON {
	internal static class Steam {
		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
		internal sealed class ConfirmationDetails {
#pragma warning disable 649
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly bool Success;
#pragma warning restore 649

			internal ulong OtherSteamID64 {
				get {
					if (_OtherSteamID64 != 0) {
						return _OtherSteamID64;
					}

					if ((Type != EType.Trade) || (OtherSteamID3 == 0)) {
						return 0;
					}

					_OtherSteamID64 = new SteamID(OtherSteamID3, EUniverse.Public, EAccountType.Individual);
					return _OtherSteamID64;
				}
			}

			internal ulong TradeOfferID {
				get {
					if (_TradeOfferID != 0) {
						return _TradeOfferID;
					}

					if ((Type != EType.Trade) || (HtmlDocument == null)) {
						return 0;
					}

					HtmlNode htmlNode = HtmlDocument.DocumentNode.SelectSingleNode("//div[@class='tradeoffer']");
					if (htmlNode == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlNode));
						return 0;
					}

					string id = htmlNode.GetAttributeValue("id", null);
					if (string.IsNullOrEmpty(id)) {
						ASF.ArchiLogger.LogNullError(nameof(id));
						return 0;
					}

					int index = id.IndexOf('_');
					if (index < 0) {
						ASF.ArchiLogger.LogNullError(nameof(index));
						return 0;
					}

					index++;
					if (id.Length <= index) {
						ASF.ArchiLogger.LogNullError(nameof(id.Length));
						return 0;
					}

					id = id.Substring(index);
					if (ulong.TryParse(id, out _TradeOfferID) && (_TradeOfferID != 0)) {
						return _TradeOfferID;
					}

					ASF.ArchiLogger.LogNullError(nameof(_TradeOfferID));
					return 0;
				}
			}

#pragma warning disable 649
			[JsonProperty(PropertyName = "html", Required = Required.DisallowNull)]
			private readonly string HTML;
#pragma warning restore 649

			private HtmlDocument HtmlDocument {
				get {
					if (_HtmlDocument != null) {
						return _HtmlDocument;
					}

					if (string.IsNullOrEmpty(HTML)) {
						return null;
					}

					_HtmlDocument = WebBrowser.StringToHtmlDocument(HTML);
					return _HtmlDocument;
				}
			}

			private uint OtherSteamID3 {
				get {
					if (_OtherSteamID3 != 0) {
						return _OtherSteamID3;
					}

					if ((Type != EType.Trade) || (HtmlDocument == null)) {
						return 0;
					}

					HtmlNode htmlNode = HtmlDocument.DocumentNode.SelectSingleNode("//a/@data-miniprofile");
					if (htmlNode == null) {
						ASF.ArchiLogger.LogNullError(nameof(htmlNode));
						return 0;
					}

					string miniProfile = htmlNode.GetAttributeValue("data-miniprofile", null);
					if (string.IsNullOrEmpty(miniProfile)) {
						ASF.ArchiLogger.LogNullError(nameof(miniProfile));
						return 0;
					}

					if (uint.TryParse(miniProfile, out _OtherSteamID3) && (_OtherSteamID3 != 0)) {
						return _OtherSteamID3;
					}

					ASF.ArchiLogger.LogNullError(nameof(_OtherSteamID3));
					return 0;
				}
			}

			private EType Type {
				get {
					if (_Type != EType.Unknown) {
						return _Type;
					}

					if (HtmlDocument == null) {
						return EType.Unknown;
					}

					HtmlNode testNode = HtmlDocument.DocumentNode.SelectSingleNode("//div[@class='mobileconf_listing_prices']");
					if (testNode != null) {
						_Type = EType.Market;
						return _Type;
					}

					testNode = HtmlDocument.DocumentNode.SelectSingleNode("//div[@class='mobileconf_trade_area']");
					if (testNode != null) {
						_Type = EType.Trade;
						return _Type;
					}

					_Type = EType.Other;
					return _Type;
				}
			}

			internal MobileAuthenticator.Confirmation Confirmation {
				get => _Confirmation;

				set {
					if (value == null) {
						ASF.ArchiLogger.LogNullError(nameof(value));
						return;
					}

					_Confirmation = value;
				}
			}

			private MobileAuthenticator.Confirmation _Confirmation;
			private HtmlDocument _HtmlDocument;
			private uint _OtherSteamID3;
			private ulong _OtherSteamID64;
			private ulong _TradeOfferID;
			private EType _Type;

			// Deserialized from JSON
			private ConfirmationDetails() { }

			internal enum EType : byte {
				Unknown,
				Trade,
				Market,
				Other
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
		internal sealed class ConfirmationResponse {
#pragma warning disable 649
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly bool Success;
#pragma warning restore 649

			// Deserialized from JSON
			private ConfirmationResponse() { }
		}

		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_Asset
		internal sealed class Item {
			internal const ushort SteamAppID = 753;
			internal const byte SteamCommunityContextID = 6;

			internal uint Amount { get; private set; }
			internal uint AppID { get; set; }
			internal ulong ClassID { get; private set; }
			internal ulong ContextID { get; set; }
			internal uint RealAppID { get; set; }
			internal EType Type { get; set; }

			private ulong AssetID;

			[JsonProperty(PropertyName = "amount", Required = Required.Always)]
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			private string AmountString {
				get => Amount.ToString();

				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));
						return;
					}

					if (!uint.TryParse(value, out uint amount) || (amount == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(amount));
						return;
					}

					Amount = amount;
				}
			}

			[JsonProperty(PropertyName = "appid", Required = Required.DisallowNull)]
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			private string AppIDString {
				get => AppID.ToString();

				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));
						return;
					}

					if (!uint.TryParse(value, out uint appID) || (appID == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(appID));
						return;
					}

					AppID = appID;
				}
			}

			[JsonProperty(PropertyName = "assetid", Required = Required.DisallowNull)]
			private string AssetIDString {
				get => AssetID.ToString();

				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));
						return;
					}

					if (!ulong.TryParse(value, out ulong assetID) || (assetID == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(assetID));
						return;
					}

					AssetID = assetID;
				}
			}

			[JsonProperty(PropertyName = "classid", Required = Required.DisallowNull)]
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			private string ClassIDString {
				get => ClassID.ToString();

				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));
						return;
					}

					if (!ulong.TryParse(value, out ulong classID) || (classID == 0)) {
						return;
					}

					ClassID = classID;
				}
			}

			[JsonProperty(PropertyName = "contextid", Required = Required.DisallowNull)]
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			private string ContextIDString {
				get => ContextID.ToString();

				set {
					if (string.IsNullOrEmpty(value)) {
						ASF.ArchiLogger.LogNullError(nameof(value));
						return;
					}

					if (!ulong.TryParse(value, out ulong contextID) || (contextID == 0)) {
						ASF.ArchiLogger.LogNullError(nameof(contextID));
						return;
					}

					ContextID = contextID;
				}
			}

			[JsonProperty(PropertyName = "id", Required = Required.DisallowNull)]
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			private string ID {
				get => AssetIDString;
				set => AssetIDString = value;
			}

			// Constructed from trades being received
			internal Item(uint appID, ulong contextID, ulong classID, uint amount, uint realAppID, EType type = EType.Unknown) {
				if ((appID == 0) || (contextID == 0) || (classID == 0) || (amount == 0) || (realAppID == 0)) {
					throw new ArgumentNullException(nameof(classID) + " || " + nameof(contextID) + " || " + nameof(classID) + " || " + nameof(amount) + " || " + nameof(realAppID));
				}

				AppID = appID;
				ContextID = contextID;
				ClassID = classID;
				Amount = amount;
				RealAppID = realAppID;
				Type = type;
			}

			// Deserialized from JSON
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			private Item() { }

			internal enum EType : byte {
				Unknown,
				BoosterPack,
				Emoticon,
				FoilTradingCard,
				ProfileBackground,
				TradingCard,
				SteamGems
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
		internal sealed class NewDiscoveryQueueResponse {
#pragma warning disable 649
			[JsonProperty(PropertyName = "queue", Required = Required.Always)]
			internal readonly HashSet<uint> Queue;
#pragma warning restore 649

			// Deserialized from JSON
			private NewDiscoveryQueueResponse() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
		internal sealed class RedeemWalletResponse {
#pragma warning disable 649
			[JsonProperty(PropertyName = "detail", Required = Required.DisallowNull)]
			internal readonly EPurchaseResultDetail? PurchaseResultDetail;
#pragma warning restore 649

#pragma warning disable 649
			[JsonProperty(PropertyName = "success", Required = Required.Always)]
			internal readonly EResult Result;
#pragma warning restore 649

			// Deserialized from JSON
			private RedeemWalletResponse() { }
		}

		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService#CEcon_TradeOffer
		internal sealed class TradeOffer {
			internal readonly HashSet<Item> ItemsToGive = new HashSet<Item>();
			internal readonly HashSet<Item> ItemsToReceive = new HashSet<Item>();
			internal readonly ETradeOfferState State;
			internal readonly ulong TradeOfferID;

			internal ulong OtherSteamID64 {
				get {
					if (_OtherSteamID64 != 0) {
						return _OtherSteamID64;
					}

					if (OtherSteamID3 == 0) {
						ASF.ArchiLogger.LogNullError(nameof(OtherSteamID3));
						return 0;
					}

					_OtherSteamID64 = new SteamID(OtherSteamID3, EUniverse.Public, EAccountType.Individual);
					return _OtherSteamID64;
				}
			}

			private readonly uint OtherSteamID3;

			private ulong _OtherSteamID64;

			// Constructed from trades being received
			internal TradeOffer(ulong tradeOfferID, uint otherSteamID3, ETradeOfferState state) {
				if ((tradeOfferID == 0) || (otherSteamID3 == 0) || (state == ETradeOfferState.Unknown)) {
					throw new ArgumentNullException(nameof(tradeOfferID) + " || " + nameof(otherSteamID3) + " || " + nameof(state));
				}

				TradeOfferID = tradeOfferID;
				OtherSteamID3 = otherSteamID3;
				State = state;
			}

			internal bool IsFairTypesExchange() {
				Dictionary<uint, Dictionary<Item.EType, uint>> itemsToGivePerGame = new Dictionary<uint, Dictionary<Item.EType, uint>>();
				foreach (Item item in ItemsToGive) {
					if (!itemsToGivePerGame.TryGetValue(item.RealAppID, out Dictionary<Item.EType, uint> itemsPerType)) {
						itemsPerType = new Dictionary<Item.EType, uint> { [item.Type] = item.Amount };
						itemsToGivePerGame[item.RealAppID] = itemsPerType;
					} else {
						if (itemsPerType.TryGetValue(item.Type, out uint amount)) {
							itemsPerType[item.Type] = amount + item.Amount;
						} else {
							itemsPerType[item.Type] = item.Amount;
						}
					}
				}

				Dictionary<uint, Dictionary<Item.EType, uint>> itemsToReceivePerGame = new Dictionary<uint, Dictionary<Item.EType, uint>>();
				foreach (Item item in ItemsToReceive) {
					if (!itemsToReceivePerGame.TryGetValue(item.RealAppID, out Dictionary<Item.EType, uint> itemsPerType)) {
						itemsPerType = new Dictionary<Item.EType, uint> {
							{ item.Type, item.Amount }
						};

						itemsToReceivePerGame[item.RealAppID] = itemsPerType;
					} else {
						if (itemsPerType.TryGetValue(item.Type, out uint amount)) {
							itemsPerType[item.Type] = amount + item.Amount;
						} else {
							itemsPerType[item.Type] = item.Amount;
						}
					}
				}

				// Ensure that amount of items to give is at least amount of items to receive (per game and per type)
				foreach (KeyValuePair<uint, Dictionary<Item.EType, uint>> itemsPerGame in itemsToGivePerGame) {
					if (!itemsToReceivePerGame.TryGetValue(itemsPerGame.Key, out Dictionary<Item.EType, uint> otherItemsPerType)) {
						return false;
					}

					foreach (KeyValuePair<Item.EType, uint> itemsPerType in itemsPerGame.Value) {
						if (!otherItemsPerType.TryGetValue(itemsPerType.Key, out uint otherAmount)) {
							return false;
						}

						if (itemsPerType.Value > otherAmount) {
							return false;
						}
					}
				}

				return true;
			}

			internal bool IsSteamCardsRequest() => ItemsToGive.All(item => (item.AppID == Item.SteamAppID) && (item.ContextID == Item.SteamCommunityContextID) && (item.Type == Item.EType.TradingCard));

			[SuppressMessage("ReSharper", "UnusedMember.Global")]
			internal enum ETradeOfferState : byte {
				Unknown,
				Invalid,
				Active,
				Accepted,
				Countered,
				Expired,
				Canceled,
				Declined,
				InvalidItems,
				EmailPending,
				EmailCanceled,
				OnHold
			}
		}

		[SuppressMessage("ReSharper", "UnusedMember.Global")]
		internal sealed class TradeOfferRequest {
			[JsonProperty(PropertyName = "me", Required = Required.Always)]
			internal readonly ItemList ItemsToGive = new ItemList();

			[JsonProperty(PropertyName = "them", Required = Required.Always)]
			internal readonly ItemList ItemsToReceive = new ItemList();

			internal sealed class ItemList {
				[JsonProperty(PropertyName = "assets", Required = Required.Always)]
				internal readonly HashSet<Item> Assets = new HashSet<Item>();
			}
		}
	}
}