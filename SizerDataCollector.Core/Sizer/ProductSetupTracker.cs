using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SizerDataCollector.Core.Db;
using SizerDataCollector.Core.Logging;

namespace SizerDataCollector.Core.Sizer
{
	/// <summary>
	/// Tracks the active product setup (variety + layout + outlet-to-product assignments)
	/// for a Sizer machine. Refreshes only on first run, batch change, or when an unknown
	/// product GUID appears at an outlet — matching Compac vendor guidance to cache heavy
	/// GetActiveVariety / GetActiveLayout calls instead of polling them on every cycle.
	/// </summary>
	public sealed class ProductSetupTracker
	{
		public const string MetricName = "product_setup";

		private readonly object _sync = new object();
		private Guid? _cachedVarietyId;
		private Guid? _cachedLayoutId;
		private HashSet<Guid> _knownProductIds = new HashSet<Guid>();
		private string _cachedSetupHash;

		/// <summary>
		/// Inspects outlet state and batch context, and returns a <see cref="MetricRow"/>
		/// describing the current product setup if a refresh was triggered and the content
		/// has changed since the last persisted snapshot. Returns null when no change is
		/// needed.
		/// </summary>
		public async Task<MetricRow> CheckAndRefreshAsync(
			ISizerClient client,
			string serialNo,
			long batchRecordId,
			CurrentBatchInfo batchInfo,
			string outletsJson,
			CancellationToken cancellationToken)
		{
			if (client == null) throw new ArgumentNullException(nameof(client));
			if (string.IsNullOrWhiteSpace(serialNo)) throw new ArgumentException("Serial must be provided.", nameof(serialNo));

			var trigger = DetermineTrigger(batchInfo, outletsJson);
			if (trigger == null)
			{
				return null;
			}

			Logger.Log($"ProductSetupTracker: refresh triggered ({trigger}) for serial '{serialNo}'.");

			Guid varietyId;
			string varietyJson;

			if (batchInfo != null && batchInfo.VarietyId != Guid.Empty)
			{
				varietyId = batchInfo.VarietyId;
				try
				{
					varietyJson = await client.GetActiveVarietyJsonAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log("ProductSetupTracker: GetActiveVariety failed; skipping setup snapshot.", ex, LogLevel.Warn);
					return null;
				}
			}
			else
			{
				try
				{
					varietyJson = await client.GetActiveVarietyJsonAsync(cancellationToken).ConfigureAwait(false);
				}
				catch (Exception ex)
				{
					Logger.Log("ProductSetupTracker: GetActiveVariety failed; skipping setup snapshot.", ex, LogLevel.Warn);
					return null;
				}

				varietyId = ExtractGuid(varietyJson, "Id");
				if (varietyId == Guid.Empty)
				{
					Logger.Log("ProductSetupTracker: active variety has no Id; cannot fetch layout. Skipping snapshot.", level: LogLevel.Warn);
					return null;
				}
			}

			string layoutJson;
			try
			{
				layoutJson = await client.GetActiveLayoutJsonAsync(varietyId, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				Logger.Log("ProductSetupTracker: GetActiveLayout failed; skipping setup snapshot.", ex, LogLevel.Warn);
				return null;
			}

			JObject varietyObj = SafeParseObject(varietyJson);
			JObject layoutObj = SafeParseObject(layoutJson);
			JArray outletsArray = SafeParseArray(outletsJson);

			var layoutId = ExtractGuid(layoutObj, "Id");
			var layoutName = layoutObj?["Name"]?.Value<string>();
			var varietyName = varietyObj?["Name"]?.Value<string>();

			var assignments = BuildAssignments(layoutObj, outletsArray);
			var products = BuildProducts(layoutObj);
			var productIds = CollectProductIds(layoutObj);

			var envelope = new JObject
			{
				["variety_id"] = varietyId,
				["variety_name"] = varietyName,
				["layout_id"] = layoutId,
				["layout_name"] = layoutName,
				["batch_id"] = batchInfo?.BatchId ?? 0,
				["batch_record_id"] = batchRecordId,
				["captured_at"] = DateTimeOffset.UtcNow.ToString("o"),
				["trigger"] = trigger,
				["assignments"] = assignments,
				["products"] = products,
				["raw_variety"] = varietyObj,
				["raw_layout"] = layoutObj
			};

			var json = envelope.ToString(Formatting.None);
			var hash = ComputeSha256(json);

			lock (_sync)
			{
				if (string.Equals(hash, _cachedSetupHash, StringComparison.Ordinal))
				{
					Logger.Log($"ProductSetupTracker: setup content unchanged after refresh ({trigger}); no row emitted.", level: LogLevel.Debug);
					return null;
				}

				_cachedVarietyId = varietyId == Guid.Empty ? (Guid?)null : varietyId;
				_cachedLayoutId = layoutId == Guid.Empty ? (Guid?)null : layoutId;
				_knownProductIds = productIds;
				_cachedSetupHash = hash;
			}

			Logger.Log($"ProductSetupTracker: emitted product_setup row (variety='{varietyName}', layout='{layoutName}', products={products.Count}, assignments={assignments.Count}).");

			return new MetricRow
			{
				Timestamp = DateTimeOffset.UtcNow,
				SerialNo = serialNo,
				BatchRecordId = batchRecordId,
				MetricName = MetricName,
				JsonPayload = json
			};
		}

		/// <summary>
		/// Clears cached state. Call when a hard reset is desired (e.g. service restart paths
		/// that reuse the same tracker instance, or explicit recommissioning).
		/// </summary>
		public void Reset()
		{
			lock (_sync)
			{
				_cachedVarietyId = null;
				_cachedLayoutId = null;
				_knownProductIds = new HashSet<Guid>();
				_cachedSetupHash = null;
			}
		}

		private string DetermineTrigger(CurrentBatchInfo batchInfo, string outletsJson)
		{
			lock (_sync)
			{
				if (_cachedSetupHash == null)
				{
					return "first_run";
				}

				if (batchInfo != null)
				{
					if (batchInfo.VarietyId != Guid.Empty && _cachedVarietyId.HasValue && batchInfo.VarietyId != _cachedVarietyId.Value)
					{
						return "variety_change";
					}

					if (batchInfo.LayoutId != Guid.Empty && _cachedLayoutId.HasValue && batchInfo.LayoutId != _cachedLayoutId.Value)
					{
						return "layout_change";
					}
				}

				if (!string.IsNullOrWhiteSpace(outletsJson) && HasUnknownProductId(outletsJson, _knownProductIds))
				{
					return "unknown_product_id";
				}

				return null;
			}
		}

		private static bool HasUnknownProductId(string outletsJson, HashSet<Guid> known)
		{
			try
			{
				var array = SafeParseArray(outletsJson);
				if (array == null) return false;

				foreach (var outlet in array)
				{
					var currentId = ExtractGuid(outlet as JObject, "CurrentProductId");
					if (currentId != Guid.Empty && !known.Contains(currentId))
					{
						return true;
					}

					var pendingId = ExtractGuid(outlet as JObject, "PendingProductId");
					if (pendingId != Guid.Empty && !known.Contains(pendingId))
					{
						return true;
					}
				}
			}
			catch
			{
				// Conservative: don't trigger refresh on parse errors.
			}

			return false;
		}

		private static JObject BuildAssignments(JObject layoutObj, JArray outletsArray)
		{
			// Build "outlet_id" -> { product_id, product_name, elements, source } map.
			// Prefer outlets payload for live state; fall back to layout.Assignments for planned.
			var result = new JObject();

			// Layout Assignments is a Dictionary<Outlet, Product>, serialized as
			// an array of KeyValueOfOutletProduct objects with Key (Outlet) and Value (Product).
			var productById = new Dictionary<Guid, JObject>();
			if (layoutObj?["Products"] is JArray productsArr)
			{
				foreach (var p in productsArr)
				{
					if (p is JObject po)
					{
						var pid = ExtractGuid(po, "Id");
						if (pid != Guid.Empty)
						{
							productById[pid] = po;
						}
					}
				}
			}

			var plannedByOutlet = new Dictionary<int, Guid>();
			if (layoutObj?["Assignments"] is JArray assignmentsArr)
			{
				foreach (var kv in assignmentsArr)
				{
					var key = kv?["Key"] as JObject;
					var val = kv?["Value"] as JObject;
					var outletId = key?["Id"]?.Value<int?>();
					var productId = ExtractGuid(val, "Id");
					if (outletId.HasValue && productId != Guid.Empty)
					{
						plannedByOutlet[outletId.Value] = productId;
					}
				}
			}

			if (outletsArray != null)
			{
				foreach (var outlet in outletsArray)
				{
					var outletId = outlet?["Id"]?.Value<int?>();
					if (!outletId.HasValue) continue;

					var liveProductId = ExtractGuid(outlet as JObject, "CurrentProductId");
					var pendingProductId = ExtractGuid(outlet as JObject, "PendingProductId");
					plannedByOutlet.TryGetValue(outletId.Value, out var plannedProductId);

					var entry = new JObject
					{
						["outlet_id"] = outletId.Value,
						["outlet_name"] = outlet?["Name"],
						["status"] = outlet?["Status"],
						["delivered_fpm"] = outlet?["DeliveredFruitPerMinute"],
						["max_rate_sqcm_per_min"] = outlet?["MaxRateSquareCMPerMinute"],
						["current_product_id"] = liveProductId == Guid.Empty ? null : (JToken)liveProductId.ToString(),
						["pending_product_id"] = pendingProductId == Guid.Empty ? null : (JToken)pendingProductId.ToString(),
						["planned_product_id"] = plannedProductId == Guid.Empty ? null : (JToken)plannedProductId.ToString(),
						["matches_plan"] = liveProductId != Guid.Empty && plannedProductId != Guid.Empty
							? (JToken)(liveProductId == plannedProductId)
							: JValue.CreateNull()
					};

					var lookupId = liveProductId != Guid.Empty ? liveProductId : plannedProductId;
					if (lookupId != Guid.Empty && productById.TryGetValue(lookupId, out var product))
					{
						entry["product_name"] = product["Name"];
						entry["product_display_name"] = product["DisplayName"];
						entry["elements"] = product["Elements"];
						entry["pack_name"] = product["Pack"]?["Name"];
					}

					result[outletId.Value.ToString()] = entry;
				}
			}
			else
			{
				// Outlets payload unavailable; emit planned assignments only.
				foreach (var pair in plannedByOutlet)
				{
					var entry = new JObject
					{
						["outlet_id"] = pair.Key,
						["planned_product_id"] = pair.Value.ToString()
					};

					if (productById.TryGetValue(pair.Value, out var product))
					{
						entry["product_name"] = product["Name"];
						entry["product_display_name"] = product["DisplayName"];
						entry["elements"] = product["Elements"];
						entry["pack_name"] = product["Pack"]?["Name"];
					}

					result[pair.Key.ToString()] = entry;
				}
			}

			return result;
		}

		private static JArray BuildProducts(JObject layoutObj)
		{
			var result = new JArray();
			if (layoutObj?["Products"] is JArray productsArr)
			{
				foreach (var p in productsArr)
				{
					if (!(p is JObject po)) continue;
					result.Add(new JObject
					{
						["id"] = po["Id"],
						["name"] = po["Name"],
						["display_name"] = po["DisplayName"],
						["special_instructions"] = po["SpecialInstructions"],
						["elements"] = po["Elements"],
						["pack"] = po["Pack"],
						["target_fill"] = po["TargetFill"]
					});
				}
			}
			return result;
		}

		private static HashSet<Guid> CollectProductIds(JObject layoutObj)
		{
			var set = new HashSet<Guid>();
			if (layoutObj?["Products"] is JArray productsArr)
			{
				foreach (var p in productsArr)
				{
					var id = ExtractGuid(p as JObject, "Id");
					if (id != Guid.Empty) set.Add(id);
				}
			}
			return set;
		}

		private static Guid ExtractGuid(string json, string property)
		{
			var obj = SafeParseObject(json);
			return ExtractGuid(obj, property);
		}

		private static Guid ExtractGuid(JObject obj, string property)
		{
			if (obj == null) return Guid.Empty;
			var token = obj[property];
			if (token == null || token.Type == JTokenType.Null) return Guid.Empty;
			var s = token.Value<string>();
			if (string.IsNullOrWhiteSpace(s)) return Guid.Empty;
			return Guid.TryParse(s, out var g) ? g : Guid.Empty;
		}

		private static JObject SafeParseObject(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try
			{
				return JToken.Parse(json) as JObject;
			}
			catch
			{
				return null;
			}
		}

		private static JArray SafeParseArray(string json)
		{
			if (string.IsNullOrWhiteSpace(json)) return null;
			try
			{
				return JToken.Parse(json) as JArray;
			}
			catch
			{
				return null;
			}
		}

		private static string ComputeSha256(string input)
		{
			using (var sha = SHA256.Create())
			{
				var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
				var hash = sha.ComputeHash(bytes);
				var sb = new StringBuilder(hash.Length * 2);
				for (int i = 0; i < hash.Length; i++)
				{
					sb.Append(hash[i].ToString("x2"));
				}
				return sb.ToString();
			}
		}
	}
}
