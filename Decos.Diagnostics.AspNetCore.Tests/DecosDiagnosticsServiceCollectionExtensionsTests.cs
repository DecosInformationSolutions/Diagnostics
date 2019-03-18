using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Decos.Diagnostics.AspNetCore.Tests
{
    [TestClass]
    public class DecosDiagnosticsServiceCollectionExtensionsTests
    {
        [TestMethod]
        public void LogFactoryCanBeResolved()
        {
            var services = new ServiceCollection();

            services.AddTraceSourceLogging();

            var provider = services.BuildServiceProvider();
            Assert.IsNotNull(provider.GetRequiredService<ILogFactory>());
        }

        [TestMethod]
        public void LogFactoryCanBeResolvedWithCustomOptions()
        {
            var services = new ServiceCollection();

            services.AddTraceSourceLogging(options =>
            {
                options.SetMinimumLogLevel(LogLevel.Debug);
            });

            var provider = services.BuildServiceProvider();
            var factory = provider.GetRequiredService<ILogFactory>();
            Assert.IsTrue(factory.Create("Test").IsEnabled(LogLevel.Debug));
        }

        [TestMethod]
        public void GenericLogCanBeResolved()
        {

            var services = new ServiceCollection();

            services.AddTraceSourceLogging();

            var provider = services.BuildServiceProvider();
            Assert.IsNotNull(provider.GetRequiredService<ILog<DecosDiagnosticsServiceCollectionExtensionsTests>>());
        }

        [TestMethod]
        public void GenericLogCanBeResolvedWithCustomOptions()
        {

            var services = new ServiceCollection();

            services.AddTraceSourceLogging(options =>
            {
                options.AddFilter(GetType().Namespace, LogLevel.None);
            });

            var provider = services.BuildServiceProvider();
            var log = provider.GetRequiredService<ILog<DecosDiagnosticsServiceCollectionExtensionsTests>>();
            Assert.IsFalse(log.IsEnabled(LogLevel.Information));
        }
    }
}