// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// POST a batch of hiccup-record captures from
    /// <see cref="osu.Game.Performance.ToriiHiccupLogger"/> to the Torii
    /// admin dashboard's ingest endpoint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The request body shape mirrors <c>HiccupBatchRequest</c> on the
    /// server (see <c>app/router/v2/torii_hiccup_reports.py</c>):
    /// per-batch metadata (session id / device hash / build info) is
    /// promoted out of the per-record payload to save ~150 bytes per
    /// record over the wire — important when a backlog flush after a
    /// long offline period might ship 50 records in one POST.
    /// </para>
    /// <para>
    /// The server's per-record validation drops bad records individually
    /// and accepts the rest, so a 200 with <c>dropped &gt; 0</c> is not
    /// an error — the logger logs the count and continues.
    /// 4xx responses surface via <see cref="APIRequest.Failure"/>; the
    /// logger treats those as transient (the same batch is re-tried on
    /// the next flush via the persisted-on-disk capture, not from
    /// memory, so a server outage doesn't lose data).
    /// </para>
    /// <para>
    /// Auth header is added automatically by <see cref="APIAccess"/> if
    /// the user is logged in. The endpoint accepts unauthed POSTs
    /// (anonymous reports keep <c>user_id = NULL</c>) so login-screen
    /// freezes — exactly the case the user can't be authenticated for
    /// — still upload.
    /// </para>
    /// </remarks>
    public class SubmitToriiHiccupReportsRequest : APIRequest<HiccupBatchResponse>
    {
        public HiccupBatchPayload Payload { get; }

        public SubmitToriiHiccupReportsRequest(HiccupBatchPayload payload)
        {
            Payload = payload;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(Payload));
            return req;
        }

        protected override string Target => @"torii/hiccup-reports";
    }

    /// <summary>Per-batch wrapper. Field names match the server's Pydantic schema.</summary>
    public class HiccupBatchPayload
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; }

        [JsonProperty("device_hash")]
        public string DeviceHash { get; set; }

        [JsonProperty("osu_version")]
        public string OsuVersion { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("cpu_arch")]
        public string CpuArch { get; set; }

        [JsonProperty("records")]
        public object[] Records { get; set; }
    }

    /// <summary>Response shape — matches the server's <c>HiccupBatchResponse</c>.</summary>
    public class HiccupBatchResponse
    {
        [JsonProperty("accepted")]
        public int Accepted { get; set; }

        [JsonProperty("dropped")]
        public int Dropped { get; set; }
    }
}
