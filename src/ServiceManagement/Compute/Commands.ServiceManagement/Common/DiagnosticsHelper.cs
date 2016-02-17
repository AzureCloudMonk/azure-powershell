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
using System.Collections;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Azure.Common.Authentication.Models;
using Microsoft.WindowsAzure.Commands.Common.Storage;
using Microsoft.WindowsAzure.Management.Storage;
using Microsoft.WindowsAzure.Management.Storage.Models;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.WindowsAzure.Commands.ServiceManagement.Common
{
    public static class DiagnosticsHelper
    {
        private static string XmlNamespace = "http://schemas.microsoft.com/ServiceHosting/2010/10/DiagnosticsConfiguration";
        private static string EncodedXmlCfg = "xmlCfg";
        private static string WadCfg = "WadCfg";
        private static string WadCfgBlob = "WadCfgBlob";
        private static string StorageAccount = "storageAccount";
        private static string Path = "path";
        private static string ExpandResourceDirectory = "expandResourceDirectory";
        private static string LocalResourceDirectory = "localResourceDirectory";
        private static string StorageAccountNameTag = "storageAccountName";
        private static string StorageAccountKeyTag = "storageAccountKey";
        private static string StorageAccountEndPointTag = "storageAccountEndPoint";

        public static string DiagnosticsConfigurationElemStr = "DiagnosticsConfiguration";
        public static string DiagnosticMonitorConfigurationElemStr = "DiagnosticMonitorConfiguration";
        public static string PublicConfigElemStr = "PublicConfig";
        public static string PrivateConfigElemStr = "PrivateConfig";
        public static string StorageAccountElemStr = "StorageAccount";
        public static string PrivConfNameAttr = "name";
        public static string PrivConfKeyAttr = "key";
        public static string PrivConfEndpointAttr = "endpoint";
        public static string MetricsElemStr = "Metrics";
        public static string MetricsResourceIdAttr = "resourceId";

        public enum ConfigFileType
        {
            Unknown,
            Json,
            Xml
        }

        public static ConfigFileType GetConfigFileType(string configurationPath)
        {
            if (!string.IsNullOrEmpty(configurationPath))
            {
                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.Load(configurationPath);
                    return ConfigFileType.Xml;
                }
                catch (XmlException)
                { }

                try
                {
                    JsonConvert.DeserializeObject(File.ReadAllText(configurationPath));
                    return ConfigFileType.Json;
                }
                catch (JsonReaderException)
                { }
            }

            return ConfigFileType.Unknown;
        }

        public static Hashtable GetPublicDiagnosticsConfigurationFromFile(string configurationPath,
            string storageAccountName, string resourceId, Cmdlet cmdlet)
        {
            switch (GetConfigFileType(configurationPath))
            {
                case ConfigFileType.Xml:
                    return GetPublicConfigFromXmlFile(configurationPath, storageAccountName, resourceId, cmdlet);
                case ConfigFileType.Json:
                    return GetPublicConfigFromJsonFile(configurationPath, storageAccountName, resourceId, cmdlet);
                default:
                    throw new ArgumentException(Properties.Resources.DiagnosticsExtensionInvalidConfigFileFormat);
            }
        }

        private static Hashtable GetPublicConfigFromXmlFile(string configurationPath, string storageAccountName, string resourceId, Cmdlet cmdlet)
        {
            var doc = XDocument.Load(configurationPath);
            var wadCfgElement = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == WadCfg);
            var wadCfgBlobElement = doc.Descendants().FirstOrDefault(d => d.Name.LocalName == WadCfgBlob);
            if (wadCfgElement == null && wadCfgBlobElement == null)
            {
                throw new ArgumentException(Properties.Resources.DiagnosticsExtensionIaaSConfigElementNotDefinedInXml);
            }

            if (wadCfgElement != null)
            {
                AutoFillMetricsConfig(wadCfgElement, resourceId, cmdlet);
            }

            string originalConfiguration = wadCfgElement != null ? wadCfgElement.ToString() : wadCfgBlobElement.ToString();
            string encodedConfiguration = Convert.ToBase64String(Encoding.UTF8.GetBytes(wadCfgElement.ToString().ToCharArray()));

            // Now extract the local resource directory element
            var node = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "LocalResourceDirectory");
            string localDirectory = (node != null && node.Attribute(Path) != null) ? node.Attribute(Path).Value : null;
            string localDirectoryExpand = (node != null && node.Attribute("expandEnvironment") != null)
                ? node.Attribute("expandEnvironment").Value
                : null;
            if (localDirectoryExpand == "0")
            {
                localDirectoryExpand = "false";
            }
            if (localDirectoryExpand == "1")
            {
                localDirectoryExpand = "true";
            }

            var hashTable = new Hashtable();
            hashTable.Add(EncodedXmlCfg, encodedConfiguration);
            hashTable.Add(StorageAccount, storageAccountName);
            if (!string.IsNullOrEmpty(localDirectory))
            {
                var localDirectoryHashTable = new Hashtable();
                localDirectoryHashTable.Add(Path, localDirectory);
                localDirectoryHashTable.Add(ExpandResourceDirectory, localDirectoryExpand);
                hashTable.Add(LocalResourceDirectory, localDirectoryHashTable);
            }

            return hashTable;
        }

        public static void AutoFillMetricsConfig(XElement wadCfgElement, string resourceId, Cmdlet cmdlet)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                return;
            }

            var configurationElem = wadCfgElement.Elements().FirstOrDefault(d => d.Name.LocalName == DiagnosticMonitorConfigurationElemStr);
            if (configurationElem == null)
            {
                throw new ArgumentException(Properties.Resources.DiagnosticsExtensionDiagnosticMonitorConfigurationElementNotDefined);
            }

            var metricsElement = configurationElem.Elements().FirstOrDefault(d => d.Name.LocalName == MetricsElemStr);
            if (metricsElement == null)
            {
                XNamespace ns = XmlNamespace;
                metricsElement = new XElement(ns + MetricsElemStr,
                    new XAttribute(MetricsResourceIdAttr, resourceId));
                configurationElem.Add(metricsElement);
            }
            else
            {
                var resourceIdAttr = metricsElement.Attribute(MetricsResourceIdAttr);
                if (resourceIdAttr != null && !resourceIdAttr.Value.Equals(resourceId))
                {
                    cmdlet.WriteWarning(Properties.Resources.DiagnosticsExtensionMetricsResourceIdNotMatch);
                }
                metricsElement.SetAttributeValue(MetricsResourceIdAttr, resourceId);
            }
        }

        private static Hashtable GetPublicConfigFromJsonFile(string configurationPath, string storageAccountName, string resourceId, Cmdlet cmdlet)
        {
            var publicConfig = GetPublicConfigJObjectFromJsonFile(configurationPath);
            var properties = publicConfig.Properties().Select(p => p.Name);
            var wadCfgProperty = properties.FirstOrDefault(p => p.Equals(WadCfg, StringComparison.OrdinalIgnoreCase));
            var wadCfgBlobProperty = properties.FirstOrDefault(p => p.Equals(WadCfgBlob, StringComparison.OrdinalIgnoreCase));
            var xmlCfgProperty = properties.FirstOrDefault(p => p.Equals(EncodedXmlCfg, StringComparison.OrdinalIgnoreCase));

            var hashTable = new Hashtable();
            hashTable.Add(StorageAccount, storageAccountName);

            if (wadCfgProperty != null && publicConfig[wadCfgProperty] is JObject)
            {
                var wadCfgObject = (JObject)publicConfig[wadCfgProperty];
                AutoFillMetricsConfig(wadCfgObject, resourceId, cmdlet);
                hashTable.Add(wadCfgProperty, wadCfgObject);
            }
            else if (wadCfgBlobProperty != null)
            {
                hashTable.Add(wadCfgBlobProperty, publicConfig[wadCfgBlobProperty]);
            }
            else if (xmlCfgProperty != null)
            {
                hashTable.Add(xmlCfgProperty, publicConfig[xmlCfgProperty]);
            }
            else
            {
                throw new ArgumentException(Properties.Resources.DiagnosticsExtensionIaaSConfigElementNotDefinedInJson);
            }

            return hashTable;
        }

        private static void AutoFillMetricsConfig(JObject wadCfgObject, string resourceId, Cmdlet cmdlet)
        {
            if (string.IsNullOrEmpty(resourceId))
            {
                return;
            }

            var configObject = wadCfgObject[DiagnosticMonitorConfigurationElemStr] as JObject;
            if (configObject == null)
            {
                throw new ArgumentException(Properties.Resources.DiagnosticsExtensionDiagnosticMonitorConfigurationElementNotDefined);
            }

            var metricsObject = configObject[MetricsElemStr] as JObject;
            if (metricsObject == null)
            {
                configObject.Add(new JProperty(MetricsElemStr,
                                    new JObject(
                                        new JProperty(MetricsResourceIdAttr, resourceId))));
            }
            else
            {
                var resourceIdValue = metricsObject[MetricsResourceIdAttr] as JValue;
                if (resourceIdValue != null && !resourceIdValue.Value.Equals(resourceId))
                {
                    cmdlet.WriteWarning(Properties.Resources.DiagnosticsExtensionMetricsResourceIdNotMatch);
                }
                metricsObject[MetricsResourceIdAttr] = resourceId;
            }
        }

        public static Hashtable GetPrivateDiagnosticsConfiguration(string storageAccountName,
            string storageKey, string endpoint)
        {
            var privateConfig = new Hashtable();
            privateConfig.Add(StorageAccountNameTag, storageAccountName);
            privateConfig.Add(StorageAccountKeyTag, storageKey);
            privateConfig.Add(StorageAccountEndPointTag, endpoint);

            return privateConfig;
        }

        private static XElement GetPublicConfigXElementFromXmlFile(string configurationPath)
        {
            XElement publicConfig = null;

            if (!string.IsNullOrEmpty(configurationPath))
            {
                var xmlConfig = XElement.Load(configurationPath);

                if (xmlConfig.Name.LocalName == PublicConfigElemStr)
                {
                    // The passed in config file is public config
                    publicConfig = xmlConfig;
                }
                else if (xmlConfig.Name.LocalName == DiagnosticsConfigurationElemStr)
                {
                    // The passed in config file is .wadcfgx file
                    publicConfig = xmlConfig.Elements().FirstOrDefault(ele => ele.Name.LocalName == PublicConfigElemStr);
                }
            }

            return publicConfig;
        }

        private static JObject GetPublicConfigJObjectFromJsonFile(string configurationPath)
        {
            var config = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(configurationPath));
            var properties = config.Properties().Select(p => p.Name);

            // If the json config has the public config as a property, we extract it. Otherwise, the root object is the public config.
            var publicConfigProperty = properties.FirstOrDefault(p => p.Equals(PublicConfigElemStr, StringComparison.OrdinalIgnoreCase));
            var publicConfig = publicConfigProperty == null ? config : config[publicConfigProperty] as JObject;

            return publicConfig;
        }

        public static string GetStorageAccountInfoFromPrivateConfig(string configurationPath, string attributeName)
        {
            string value = null;
            var configFileType = GetConfigFileType(configurationPath);

            if (configFileType == ConfigFileType.Xml)
            {
                var xmlConfig = XElement.Load(configurationPath);

                if (xmlConfig.Name.LocalName == DiagnosticsConfigurationElemStr)
                {
                    var privateConfigElem = xmlConfig.Elements().FirstOrDefault(ele => ele.Name.LocalName == PrivateConfigElemStr);
                    var storageAccountElem = privateConfigElem == null ? null : privateConfigElem.Elements().FirstOrDefault(ele => ele.Name.LocalName == StorageAccountElemStr);
                    var attribute = storageAccountElem == null ? null : storageAccountElem.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, attributeName));
                    value = attribute == null ? null : attribute.Value;
                }
            }
            else if (configFileType == ConfigFileType.Json)
            {
                var jsonConfig = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(configurationPath));
                var properties = jsonConfig.Properties().Select(p => p.Name);

                var privateConfigProperty = properties.FirstOrDefault(p => p.Equals(PrivateConfigElemStr, StringComparison.OrdinalIgnoreCase));
                if (privateConfigProperty != null)
                {
                    var privateConfig = jsonConfig[privateConfigProperty] as JObject;
                    properties = privateConfig.Properties().Select(p => p.Name);

                    var attributeProperty = properties.FirstOrDefault(p => p.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
                    value = attributeProperty == null ? null : privateConfig[attributeProperty].Value<string>();
                }
            }

            return value;
        }

        /// <summary>
        /// Initialize the storage account name if it's not specified.
        /// It can be defined in multiple places, we only take the one with higher precedence. And the precedence is:
        /// 1. The one get from StorageContext parameter
        /// 2. The one parsed from the diagnostics configuration file
        /// </summary>
        public static string InitializeStorageAccountName(AzureStorageContext storageContext = null, string configurationPath = null)
        {
            string storageAccountName = null;
            var configFileType = GetConfigFileType(configurationPath);

            if (storageContext != null)
            {
                storageAccountName = storageContext.StorageAccountName;
            }
            else if (configFileType == ConfigFileType.Xml)
            {
                var publicConfig = GetPublicConfigXElementFromXmlFile(configurationPath);
                var storageNode = publicConfig == null ? null : publicConfig.Elements().FirstOrDefault(ele => ele.Name.LocalName == StorageAccountElemStr);
                storageAccountName = storageNode == null ? null : storageNode.Value;
            }
            else if (configFileType == ConfigFileType.Json)
            {
                var publicConfig = GetPublicConfigJObjectFromJsonFile(configurationPath);
                var properties = publicConfig.Properties().Select(p => p.Name);
                var storageAccountProperty = properties.FirstOrDefault(p => p.Equals(StorageAccount, StringComparison.OrdinalIgnoreCase));
                storageAccountName = storageAccountProperty == null ? null : publicConfig[storageAccountProperty].Value<string>();
            }

            return storageAccountName;
        }

        /// <summary>
        /// Initialize the storage account key if it's not specified.
        /// It can be defined in multiple places, we only take the one with higher precedence. And the precedence is:
        /// 1. The one we try to resolve within current subscription
        /// 2. The one defined in PrivateConfig in the configuration file
        /// </summary>
        public static string InitializeStorageAccountKey(IStorageManagementClient storageClient, string storageAccountName = null, string configurationPath = null)
        {
            string storageAccountKey = null;
            StorageAccount storageAccount = null;

            if (TryGetStorageAccount(storageClient, storageAccountName, out storageAccount))
            {
                // Help user retrieve the storage account key
                var keys = storageClient.StorageAccounts.GetKeys(storageAccount.Name);
                if (keys != null)
                {
                    storageAccountKey = !string.IsNullOrEmpty(keys.PrimaryKey) ? keys.PrimaryKey : keys.SecondaryKey;
                }
            }
            else
            {
                // Use the one defined in PrivateConfig
                storageAccountKey = GetStorageAccountInfoFromPrivateConfig(configurationPath, PrivConfKeyAttr);
            }

            return storageAccountKey;
        }

        /// <summary>
        /// Initialize the storage account endpoint if it's not specified.
        /// We can get the value from multiple places, we only take the one with higher precedence. And the precedence is:
        /// 1. The one get from StorageContext parameter
        /// 2. The one get from the storage account
        /// 3. The one get from PrivateConfig element in config file
        /// 4. The one get from current Azure Environment
        /// </summary>
        public static string InitializeStorageAccountEndpoint(string storageAccountName, string storageAccountKey, IStorageManagementClient storageClient,
            AzureStorageContext storageContext = null, string configurationPath = null, AzureContext defaultContext = null)
        {
            string storageAccountEndpoint = null;
            StorageAccount storageAccount = null;

            if (storageContext != null)
            {
                // Get value from StorageContext
                storageAccountEndpoint = GetEndpointFromStorageContext(storageContext);
            }
            else if (TryGetStorageAccount(storageClient, storageAccountName, out storageAccount)
                && storageAccount.Properties.Endpoints.Count >= 4)
            {
                // Get value from StorageAccount
                var endpoints = storageAccount.Properties.Endpoints;
                var context = CreateStorageContext(endpoints[0], endpoints[1], endpoints[2], endpoints[3], storageAccountName, storageAccountKey);
                storageAccountEndpoint = GetEndpointFromStorageContext(context);
            }
            else if (!string.IsNullOrEmpty(
                storageAccountEndpoint = GetStorageAccountInfoFromPrivateConfig(configurationPath, PrivConfEndpointAttr)))
            {
                // We can get value from PrivateConfig
            }
            else if (defaultContext != null && defaultContext.Environment != null)
            {
                // Get value from default azure environment. Default to use https
                Uri blobEndpoint = defaultContext.Environment.GetStorageBlobEndpoint(storageAccountName);
                Uri queueEndpoint = defaultContext.Environment.GetStorageQueueEndpoint(storageAccountName);
                Uri tableEndpoint = defaultContext.Environment.GetStorageTableEndpoint(storageAccountName);
                Uri fileEndpoint = defaultContext.Environment.GetStorageFileEndpoint(storageAccountName);
                var context = CreateStorageContext(blobEndpoint, queueEndpoint, tableEndpoint, fileEndpoint, storageAccountName, storageAccountKey);
                storageAccountEndpoint = GetEndpointFromStorageContext(context);
            }

            return storageAccountEndpoint;
        }

        private static bool TryGetStorageAccount(IStorageManagementClient storageClient, string storageAccountName, out StorageAccount storageAccount)
        {
            try
            {
                storageAccount = storageClient.StorageAccounts.Get(storageAccountName).StorageAccount;
            }
            catch
            {
                storageAccount = null;
            }

            return storageAccount != null;
        }

        private static AzureStorageContext CreateStorageContext(Uri blobEndpoint, Uri queueEndpoint, Uri tableEndpoint, Uri fileEndpoint,
            string storageAccountName, string storageAccountKey)
        {
            var credentials = new StorageCredentials(storageAccountName, storageAccountKey);
            var cloudStorageAccount = new CloudStorageAccount(credentials, blobEndpoint, queueEndpoint, tableEndpoint, fileEndpoint);
            return new AzureStorageContext(cloudStorageAccount);
        }

        private static string GetEndpointFromStorageContext(AzureStorageContext context)
        {
            var scheme = context.BlobEndPoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "https://" : "http://";
            return scheme + context.EndPointSuffix;
        }
    }
}
