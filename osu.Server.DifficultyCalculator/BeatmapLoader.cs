// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using CSRedis;
using Dapper;
using McMaster.Extensions.CommandLineUtils;
using MySqlConnector;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Network;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.IO;
using osu.Game.Rulesets.Catch;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Taiko;
using Decoder = osu.Game.Beatmaps.Formats.Decoder;
using WebRequest = osu.Framework.IO.Network.WebRequest;

namespace osu.Server.DifficultyCalculator
{
    public static class BeatmapLoader
    {
        public static byte[] getBeatmapFileDataByBeatmapIdFromSayobotApi(int BeatmapId)
        {
            using var wc = new WebClient();
            bool _failed;
            do
            {
                _failed = false;
                try
                {   //Tell SayobotApi who are we
                    wc.Headers.Add(HttpRequestHeader.UserAgent, "osu!ude");
                    wc.Headers.Add(HttpRequestHeader.Referer, "https://osu.zhzi233.cn");


                    var setid = getSetIdByBeatmapId(BeatmapId);
                    var beatmapSetInfo =
                        FromUrl<dynamic>($"https://dl.sayobot.cn/beatmaps/info/{setid}");
                    List<dynamic> beatmapSetData = new List<dynamic>(beatmapSetInfo.data);
                    var _beatmapHashFromScore = getBeatmapChecksumByBeatmapId(BeatmapId);
                    var _beatmapFilenameFromScore = getFilenameByBeatmapId(BeatmapId);
                    var beatmapData = beatmapSetData
                        .First(data => data.beatmapID == BeatmapId
                        || data.file_md5 == _beatmapHashFromScore
                        || data.file_name == _beatmapFilenameFromScore);

                    string beatmapFileName = beatmapData.file_name.Value;
                    var beatmapFileNameAscii = Uri.EscapeDataString(beatmapFileName);

                    var downladUrl
                        = $"https://dl.sayobot.cn/beatmaps/files/{setid}/{beatmapFileNameAscii}";
                    //RedisHelper.Set("test", downladUrl);
                    var fileData = wc.DownloadData(downladUrl);

                    return fileData;
                }
                catch (Exception e)
                {
                    _failed = true;
                    Logger.Error(e, "从小夜api获取铺面时抛出了异常");
                }
            } while (false/*_failed*/);
            return null;

        }

        public static byte[] readBeatmapByBeatmapIdFromRedis(int BeatmapId)
        {
            return RedisHelper.HGet<byte[]>("beatmap", BeatmapId.ToString());
        }
        public static uint getSetIdByBeatmapId(int bid)
        {
            uint fromSayobot()
            {
                var infoV2 = FromUrl<dynamic>($"https://api.sayobot.cn/v2/beatmapinfo?K={bid}&T=1");
                return infoV2.data.sid;
            }

            using var conn = Database.GetConnection();

            uint? fromDatabase()
                => conn.QuerySingle<uint?>($"SELECT beatmapset_id FROM osu_beatmaps WHERE beatmap_id={bid}");

            return fromDatabase() ?? fromSayobot();
        }

        public static string getFilenameByBeatmapId(int bid)
        {
            using var conn = Database.GetConnection();
            return conn.QuerySingle<string>($"SELECT filename from osu_beatmaps where beatmap_id={bid}");
        }

        public static T FromUrl<T>(string url)
        {
            using var jsonReq = new JsonWebRequest<T>(url) { AllowRetryOnTimeout = true };
            jsonReq.AddHeader("user-agent", "osu!ude");
            jsonReq.AddHeader("referer", "https://zhzi233.cn");
            jsonReq.Perform();
            return jsonReq.ResponseObject;
        }

        public static string getBeatmapChecksumByBeatmapId(int bid)
        {
            string fromPpy()
            {
                return FromUrl<dynamic>($"https://old.ppy.sh/api/get_beatmaps?k={apikey}&b={bid}")[0].file_md5;
            }
            string fromDatabase()
            {
                using var conn = Database.GetConnection();
                return conn.QuerySingle<string>($"SELECT checksum FROM osu_beatmaps WHERE beatmap_id={bid}") as string;
            }

            return fromDatabase() ?? fromPpy();
        }

        public static string ToAscii(string orginal)
        {
            var asciiData = Encoding.ASCII.GetBytes(orginal);
            var sb = new StringBuilder();
            asciiData.ForEach(data => sb.Append($"%{data:X}"));
            return sb.ToString();
        }

        public static Stream GetBeatmapByBid(int BeatmapId)
        {
            byte[] _beatmapData = readBeatmapByBeatmapIdFromRedis(BeatmapId);
            if (_beatmapData is null)
            {
                _beatmapData = getBeatmapFileDataByBeatmapIdFromSayobotApi(BeatmapId);

                if (_beatmapData == null)
                    return null;
                //cache the result to redis
                RedisHelper.HSetAsync("beatmap", BeatmapId.ToString(), _beatmapData);

            }
            var stream = new MemoryStream(_beatmapData);


            return stream;
        }

        public static WorkingBeatmap GetBeatmap(int beatmapId, bool verbose = false, bool forceDownload = true, IReporter reporter = null)
        {

            string fileLocation = Path.Combine(AppSettings.BEATMAPS_PATH, beatmapId.ToString()) + ".osu";

            if ((forceDownload || !File.Exists(fileLocation)) && AppSettings.ALLOW_DOWNLOAD)
            {
                Stream stream;
                if (verbose)
                    reporter?.Verbose($"Downloading {beatmapId}.");
                stream = GetBeatmapByBid(beatmapId);
                if (stream == null)
                {
                    var req = new WebRequest(string.Format(AppSettings.DOWNLOAD_PATH, beatmapId))
                    {
                        AllowInsecureRequests = true,
                        
                    };

                    req.Failed += _ =>
                    {
                        if (verbose)
                            reporter?.Error($"Failed to download {beatmapId}.");
                    };

                    req.Finished += () =>
                    {
                        if (verbose)
                            reporter?.Verbose($"{beatmapId} successfully downloaded.");
                    };

                    req.Perform();
                    RedisHelper.HSet("beatmap", beatmapId.ToString(), req.GetResponseData());
                    stream = req.ResponseStream;
                }
                if (AppSettings.SAVE_DOWNLOADED)
                {
                    using (var fileStream = File.Create(fileLocation))
                    {
                        stream.CopyTo(fileStream);
                        stream.Seek(0, SeekOrigin.Begin);
                    }
                }

                return new LoaderWorkingBeatmap(stream);
            }

            return !File.Exists(fileLocation) ? null : new LoaderWorkingBeatmap(fileLocation);

        }

        private class LoaderWorkingBeatmap : WorkingBeatmap
        {
            private readonly Beatmap beatmap;

            /// <summary>
            /// Constructs a new <see cref="LoaderWorkingBeatmap"/> from a .osu file.
            /// </summary>
            /// <param name="file">The .osu file.</param>
            public LoaderWorkingBeatmap(string file)
                : this(File.OpenRead(file))
            {
            }

            public LoaderWorkingBeatmap(Stream stream)
                : this(new LineBufferedReader(stream))
            {
                stream.Dispose();
            }

            private LoaderWorkingBeatmap(LineBufferedReader reader)
                : this(Decoder.GetDecoder<Beatmap>(reader).Decode(reader))
            {
            }

            private LoaderWorkingBeatmap(Beatmap beatmap)
                : base(beatmap.BeatmapInfo, null)
            {
                this.beatmap = beatmap;

                switch (beatmap.BeatmapInfo.RulesetID)
                {
                    case 0:
                        beatmap.BeatmapInfo.Ruleset = new OsuRuleset().RulesetInfo;
                        break;

                    case 1:
                        beatmap.BeatmapInfo.Ruleset = new TaikoRuleset().RulesetInfo;
                        break;

                    case 2:
                        beatmap.BeatmapInfo.Ruleset = new CatchRuleset().RulesetInfo;
                        break;

                    case 3:
                        beatmap.BeatmapInfo.Ruleset = new ManiaRuleset().RulesetInfo;
                        break;
                }
            }

            protected override IBeatmap GetBeatmap() => beatmap;
            protected override Texture GetBackground() => null;
            protected override Track GetBeatmapTrack() => null;

        }
    }
}
