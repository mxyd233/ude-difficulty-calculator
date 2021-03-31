﻿// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Server.DifficultyCalculator
{
    public static class AppSettings
    {
        /// <summary>
        /// Whether to insert entries into the beatmaps table should they not exist. Should be false for production (beatmaps should already exist).
        /// </summary>
        public static readonly bool INSERT_BEATMAPS;

        /// <summary>
        /// A full or relative path used to store beatmaps.
        /// </summary>
        public static readonly string BEATMAPS_PATH;

        /// <summary>
        /// Whether beatmaps should be downloaded if they don't exist in <see cref="BEATMAPS_PATH"/>.
        /// </summary>
        public static readonly bool ALLOW_DOWNLOAD;

        /// <summary>
        /// A URL used to download beatmaps with {0} being replaced with the beatmap_id.
        /// ie. "https://osu.ppy.sh/osu/{0}"
        /// </summary>
        public static readonly string DOWNLOAD_PATH;

        /// <summary>
        /// Whether downloaded files should be cached to <see cref="BEATMAPS_PATH"/>.
        /// </summary>
        public static readonly bool SAVE_DOWNLOADED;

        /// <summary>
        /// Whether the difficulty command should wait for docker to be ready and perform automatic operations.
        /// </summary>
        public static readonly bool RUN_AS_SANDBOX_DOCKER;

        static AppSettings()
        {
            INSERT_BEATMAPS = Environment.GetEnvironmentVariable("INSERT_BEATMAPS") == "1";
            ALLOW_DOWNLOAD =true;//= Environment.GetEnvironmentVariable("ALLOW_DOWNLOAD") == "1";
            SAVE_DOWNLOADED = Environment.GetEnvironmentVariable("SAVE_DOWNLOADED") == "1";

            BEATMAPS_PATH = Environment.GetEnvironmentVariable("BEATMAPS_PATH") ?? "osu";
            DOWNLOAD_PATH = Environment.GetEnvironmentVariable("DOWNLOAD_PATH") ?? "https://osu.ppy.sh/osu/{0}";

            RUN_AS_SANDBOX_DOCKER = Environment.GetEnvironmentVariable("DOCKER") == "1";
        }
    }
}
