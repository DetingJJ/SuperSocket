﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SuperSocket.SocketEngine.Configuration;
using SuperSocket.SocketBase.Provider;
using SuperSocket.SocketBase.Config;

namespace SuperSocket.SocketEngine
{
    class WorkItemFactoryInfoLoader
    {
        private ProviderFactoryInfo m_DefaultLogFactoryFactory;

        private IConfigurationSource m_Config;

        public WorkItemFactoryInfoLoader(IConfigurationSource config)
        {
            m_Config = config;
        }

        public ProviderFactoryInfo GetLogFactoryFactory()
        {
            if (string.IsNullOrEmpty(m_Config.LogFactory))
                return null;

            ProviderFactoryInfo factory = null;

            if (m_Config.LogFactories != null && m_Config.LogFactories.Count() > 0)
            {
                var logConfig = m_Config.LogFactories.FirstOrDefault(f =>
                    f.Name.Equals(m_Config.LogFactory, StringComparison.OrdinalIgnoreCase));

                if (logConfig != null)
                {
                    factory = new ProviderFactoryInfo(ProviderKey.LogFactory, m_Config.LogFactory, GetTypeByTypeProvider(ProviderKey.LogFactory, logConfig));
                }
            }

            if (factory == null)
                throw new Exception(string.Format("the specific log factory '{0}' cannot be found!", m_Config.LogFactory));

            m_DefaultLogFactoryFactory = factory;

            return factory;
        }

        public List<WorkItemFactoryInfo> LoadResult(Func<IServerConfig, IServerConfig> serverConfigResolver)
        {
            var workItemFactories = new List<WorkItemFactoryInfo>(m_Config.Servers.Count());

            var providerFactories = new List<ProviderFactoryInfo>();

            //Initialize services
            var serviceFactories = InitializeProviderFactories(ProviderKey.Service, m_Config.Services);

            if (serviceFactories == null || !serviceFactories.Any())
                throw new Exception("Services configuration node is required!");

            //Initialize connection filters
            var connectionFilterFactories = InitializeProviderFactories(ProviderKey.ConnectionFilter, m_Config.ConnectionFilters);

            if (connectionFilterFactories != null && connectionFilterFactories.Any())
                providerFactories.AddRange(connectionFilterFactories);

            //Initialize log factories
            var logFactoryFactories = InitializeProviderFactories(ProviderKey.LogFactory, m_Config.LogFactories,
                m_DefaultLogFactoryFactory != null ? new string[] { m_DefaultLogFactoryFactory.Name } : new string[0]);

            if (m_DefaultLogFactoryFactory != null)
                logFactoryFactories.Add(m_DefaultLogFactoryFactory);

            //Initialize request filter factories
            var requestFilterFactories = InitializeProviderFactories(ProviderKey.RequestFilterFactory, m_Config.RequestFilterFactories);

            if (requestFilterFactories != null && requestFilterFactories.Any())
                providerFactories.AddRange(requestFilterFactories);

            //Initialize servers
            foreach (var c in m_Config.Servers.OrderBy(s => s.StartupOrder))
            {
                var serverConfig = serverConfigResolver(c);

                if (string.IsNullOrEmpty(serverConfig.Name))
                    throw new Exception("The name attribute of server node is required!");

                if (string.IsNullOrEmpty(serverConfig.ServiceName))
                    throw new Exception("The serviceName attribute of server node is required!");

                var serviceFactory = providerFactories.FirstOrDefault(p =>
                        p.Key == ProviderKey.Service && p.Name.Equals(serverConfig.ServiceName, StringComparison.OrdinalIgnoreCase));

                if (serviceFactory == null)
                    throw new Exception(string.Format("Failed to find a service for server {0}!", serverConfig.Name));

                var workItemFactory = new WorkItemFactoryInfo();
                workItemFactory.Config = serverConfig;
                workItemFactory.ServiceType = serviceFactory.ExportFactory.Type;

                var factories = new List<ProviderFactoryInfo>();

                //Initialize connection filters
                if(!string.IsNullOrEmpty(serverConfig.ConnectionFilter))
                {
                    var filters = serverConfig.ConnectionFilter.Split(new char[] { ',', ';' });

                    if(filters != null && filters.Any())
                    {
                        filters = filters.Select(f => f.Trim()).ToArray();
                        var filterFactories = connectionFilterFactories.Where(p =>
                                filters.Contains(p.Name, StringComparer.OrdinalIgnoreCase));

                        if (filterFactories.Any())
                            factories.AddRange(filterFactories);
                    }
                }

                var logFactoryName = ((Server)c).LogFactory;

                if (!string.IsNullOrEmpty(logFactoryName))
                {
                    logFactoryName = logFactoryName.Trim();

                    var logFactoryFactory = logFactoryFactories.FirstOrDefault(p => p.Name.Equals(logFactoryName, StringComparison.OrdinalIgnoreCase));

                    if (logFactoryFactory == null)
                        throw new Exception(string.Format("the specific log factory '{0}' cannot be found!", logFactoryName));

                    factories.Add(logFactoryFactory);
                }
                else if (m_DefaultLogFactoryFactory != null)
                {
                    factories.Add(m_DefaultLogFactoryFactory);
                }

                workItemFactory.ProviderFactories = factories;

                workItemFactories.Add(workItemFactory);
            }

            return workItemFactories;
        }

        private List<ProviderFactoryInfo> InitializeProviderFactories(ProviderKey key, IEnumerable<ITypeProvider> providerCollection, params string[] ignoreNames)
        {
            if (providerCollection == null || !providerCollection.Any())
                return null;

            var factories = new List<ProviderFactoryInfo>(providerCollection.Count());

            foreach (var provider in providerCollection)
            {
                if (ignoreNames != null && ignoreNames.Length > 0)
                {
                    if (ignoreNames.Contains(provider.Name, StringComparer.OrdinalIgnoreCase))
                        continue;
                }

                factories.Add(new ProviderFactoryInfo(key, provider.Name, GetTypeByTypeProvider(key, provider)));
            }

            return factories;
        }

        private Type GetTypeByTypeProvider(ProviderKey key, ITypeProvider provider)
        {
            try
            {
                var providerType = Type.GetType(provider.Type, true);

                if (providerType == null)
                    throw new NullReferenceException();

                return providerType;
            }
            catch (Exception e)
            {
                throw new Exception(string.Format("Failed to get {0} {1}'s type {2}.", key.Name, provider.Name, provider.Type), e);
            }
        }
    }
}
