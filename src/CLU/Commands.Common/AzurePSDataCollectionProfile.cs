﻿// ----------------------------------------------------------------------------------
//
// Copyright Microsoft Corporation
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// ----------------------------------------------------------------------------------

using System;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Azure.Common.Authentication;

namespace Microsoft.Azure.Commands.Common
{
    public class AzurePSDataCollectionProfile
    {
        public const string EnvironmentVariableName = "Azure_PS_Data_Collection";
        public static string DefaultFileName = "AzureDataCollectionProfile.json";

        public AzurePSDataCollectionProfile()
        {
        }

        public AzurePSDataCollectionProfile(bool enable)
        {
            EnableAzureDataCollection = enable;
        }

        [JsonProperty(PropertyName = "enableAzureDataCollection")]
        public bool? EnableAzureDataCollection { get; set; }
    }
}
