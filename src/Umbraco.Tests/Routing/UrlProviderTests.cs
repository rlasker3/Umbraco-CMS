﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Moq;
using NUnit.Framework;
using Umbraco.Core.Configuration.UmbracoSettings;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Tests.TestHelpers;
using Umbraco.Tests.TestHelpers.Stubs;
using Umbraco.Tests.Testing;
using Umbraco.Web.PublishedCache;
using Umbraco.Web.PublishedCache.XmlPublishedCache;
using Umbraco.Web.Routing;

namespace Umbraco.Tests.Routing
{
    [TestFixture]
    [UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerFixture)]
    public class UrlProviderTests : BaseWebTest
    {
        protected override void Compose()
        {
            base.Compose();
            Container.Register<ISiteDomainHelper, SiteDomainHelper>();
        }

        private IUmbracoSettingsSection _umbracoSettings;

        public override void SetUp()
        {
            base.SetUp();

            //generate new mock settings and assign so we can configure in individual tests
            _umbracoSettings = SettingsForTests.GenerateMockUmbracoSettings();
            SettingsForTests.ConfigureSettings(_umbracoSettings);
        }

        /// <summary>
        /// This checks that when we retrieve a NiceUrl for multiple items that there are no issues with cache overlap
        /// and that they are all cached correctly.
        /// </summary>
        [Test]
        public void Ensure_Cache_Is_Correct()
        {
            var globalSettings = Mock.Get(TestObjects.GetGlobalSettings()); //this will modify the IGlobalSettings instance stored in the container
            globalSettings.Setup(x => x.UseDirectoryUrls).Returns(true);
            globalSettings.Setup(x => x.HideTopLevelNodeFromPath).Returns(false);
            SettingsForTests.ConfigureSettings(globalSettings.Object);

            var umbracoContext = GetUmbracoContext("/test", 1111, urlProviders: new[]
            {
                new DefaultUrlProvider(_umbracoSettings.RequestHandler, Logger, globalSettings.Object, new SiteDomainHelper())
            }, globalSettings: globalSettings.Object);

            var requestHandlerMock = Mock.Get(_umbracoSettings.RequestHandler);
            requestHandlerMock.Setup(x => x.AddTrailingSlash).Returns(false);// (cached routes have none)

            var samples = new Dictionary<int, string> {
                { 1046, "/home" },
                { 1173, "/home/sub1" },
                { 1174, "/home/sub1/sub2" },
                { 1176, "/home/sub1/sub-3" },
                { 1177, "/home/sub1/custom-sub-1" },
                { 1178, "/home/sub1/custom-sub-2" },
                { 1175, "/home/sub-2" },
                { 1172, "/test-page" }
            };

            foreach (var sample in samples)
            {
                var result = umbracoContext.UrlProvider.GetUrl(sample.Key);
                Assert.AreEqual(sample.Value, result);
            }

            var randomSample = new KeyValuePair<int, string>(1177, "/home/sub1/custom-sub-1");
            for (int i = 0; i < 5; i++)
            {
                var result = umbracoContext.UrlProvider.GetUrl(randomSample.Key);
                Assert.AreEqual(randomSample.Value, result);
            }

            var cache = umbracoContext.ContentCache as PublishedContentCache;
            if (cache == null) throw new Exception("Unsupported IPublishedContentCache, only the Xml one is supported.");
            var cachedRoutes = cache.RoutesCache.GetCachedRoutes();
            Assert.AreEqual(8, cachedRoutes.Count);

            foreach (var sample in samples)
            {
                Assert.IsTrue(cachedRoutes.ContainsKey(sample.Key));
                Assert.AreEqual(sample.Value, cachedRoutes[sample.Key]);
            }

            var cachedIds = cache.RoutesCache.GetCachedIds();
            Assert.AreEqual(0, cachedIds.Count);
        }

        // test hideTopLevelNodeFromPath false
        [TestCase(1046, "/home/")]
        [TestCase(1173, "/home/sub1/")]
        [TestCase(1174, "/home/sub1/sub2/")]
        [TestCase(1176, "/home/sub1/sub-3/")]
        [TestCase(1177, "/home/sub1/custom-sub-1/")]
        [TestCase(1178, "/home/sub1/custom-sub-2/")]
        [TestCase(1175, "/home/sub-2/")]
        [TestCase(1172, "/test-page/")]
        public void Get_Url_Not_Hiding_Top_Level(int nodeId, string niceUrlMatch)
        {
            var globalSettings = Mock.Get(TestObjects.GetGlobalSettings()); //this will modify the IGlobalSettings instance stored in the container
            globalSettings.Setup(x => x.UseDirectoryUrls).Returns(true);
            globalSettings.Setup(x => x.HideTopLevelNodeFromPath).Returns(false);
            SettingsForTests.ConfigureSettings(globalSettings.Object);

            var umbracoContext = GetUmbracoContext("/test", 1111, urlProviders: new[]
            {
                new DefaultUrlProvider(_umbracoSettings.RequestHandler, Logger, globalSettings.Object, new SiteDomainHelper())
            }, globalSettings: globalSettings.Object);

            var requestMock = Mock.Get(_umbracoSettings.RequestHandler);
            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);

            var result = umbracoContext.UrlProvider.GetUrl(nodeId);
            Assert.AreEqual(niceUrlMatch, result);
        }

        // no need for umbracoUseDirectoryUrls test = should be handled by UriUtilityTests

        // test hideTopLevelNodeFromPath true
        [TestCase(1046, "/")]
        [TestCase(1173, "/sub1/")]
        [TestCase(1174, "/sub1/sub2/")]
        [TestCase(1176, "/sub1/sub-3/")]
        [TestCase(1177, "/sub1/custom-sub-1/")]
        [TestCase(1178, "/sub1/custom-sub-2/")]
        [TestCase(1175, "/sub-2/")]
        [TestCase(1172, "/test-page/")] // not hidden because not first root
        public void Get_Url_Hiding_Top_Level(int nodeId, string niceUrlMatch)
        {
            var globalSettings = Mock.Get(TestObjects.GetGlobalSettings()); //this will modify the IGlobalSettings instance stored in the container
            globalSettings.Setup(x => x.UseDirectoryUrls).Returns(true);
            globalSettings.Setup(x => x.HideTopLevelNodeFromPath).Returns(true);
            SettingsForTests.ConfigureSettings(globalSettings.Object);

            var umbracoContext = GetUmbracoContext("/test", 1111, urlProviders: new[]
            {
                new DefaultUrlProvider(_umbracoSettings.RequestHandler, Logger, globalSettings.Object, new SiteDomainHelper())
            }, globalSettings: globalSettings.Object);

            var requestMock = Mock.Get(_umbracoSettings.RequestHandler);
            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);

            var result = umbracoContext.UrlProvider.GetUrl(nodeId);
            Assert.AreEqual(niceUrlMatch, result);
        }

        /// <summary>
        /// This tests DefaultUrlProvider.GetUrl with a specific culture when the current URL is the culture specific domain
        /// </summary>
        [Test]
        public void Get_Url_For_Culture_Variant_With_Current_Url()
        {
            const string currentUri = "http://example.fr/test";

            var globalSettings = Mock.Get(TestObjects.GetGlobalSettings()); //this will modify the IGlobalSettings instance stored in the container
            globalSettings.Setup(x => x.UseDirectoryUrls).Returns(true);
            globalSettings.Setup(x => x.HideTopLevelNodeFromPath).Returns(false);
            SettingsForTests.ConfigureSettings(globalSettings.Object);

            var requestMock = Mock.Get(_umbracoSettings.RequestHandler);
            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);

            var publishedContentCache = new Mock<IPublishedContentCache>();
            publishedContentCache.Setup(x => x.GetRouteById(1234, "fr-FR"))
                .Returns("9876/home/test-fr"); //prefix with the root id node with the domain assigned as per the umbraco standard

            var domainCache = new Mock<IDomainCache>();
            domainCache.Setup(x => x.GetAssigned(It.IsAny<int>(), false))
                .Returns((int contentId, bool includeWildcards) =>
                {
                    if (contentId != 9876) return Enumerable.Empty<Domain>();
                    return new[]
                    {
                        new Domain(2, "example.us", 9876, CultureInfo.GetCultureInfo("en-US"), false, true), //default
                        new Domain(3, "example.fr", 9876, CultureInfo.GetCultureInfo("fr-FR"), false, true)
                    };
                });

            var snapshot = Mock.Of<IPublishedSnapshot>(x => x.Content == publishedContentCache.Object && x.Domains == domainCache.Object);

            var snapshotService = new Mock<IPublishedSnapshotService>();
            snapshotService.Setup(x => x.CreatePublishedSnapshot(It.IsAny<string>()))
                .Returns(snapshot);

            var umbracoContext = GetUmbracoContext(currentUri, umbracoSettings: _umbracoSettings,
                urlProviders: new[] {
                    new DefaultUrlProvider(_umbracoSettings.RequestHandler, Logger, globalSettings.Object, new SiteDomainHelper())
                },
                globalSettings: globalSettings.Object,
                snapshotService: snapshotService.Object);


            var url = umbracoContext.UrlProvider.GetUrl(1234, "fr-FR");

            Assert.AreEqual("/home/test-fr/", url);
        }

        /// <summary>
        /// This tests DefaultUrlProvider.GetUrl with a specific culture when the current URL is not the culture specific domain
        /// </summary>
        [Test]
        public void Get_Url_For_Culture_Variant_Non_Current_Url()
        {
            const string currentUri = "http://example.us/test";

            var globalSettings = Mock.Get(TestObjects.GetGlobalSettings()); //this will modify the IGlobalSettings instance stored in the container
            globalSettings.Setup(x => x.UseDirectoryUrls).Returns(true);
            globalSettings.Setup(x => x.HideTopLevelNodeFromPath).Returns(false);
            SettingsForTests.ConfigureSettings(globalSettings.Object);

            var requestMock = Mock.Get(_umbracoSettings.RequestHandler);
            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);

            var publishedContentCache = new Mock<IPublishedContentCache>();
            publishedContentCache.Setup(x => x.GetRouteById(1234, "fr-FR"))
                .Returns("9876/home/test-fr"); //prefix with the root id node with the domain assigned as per the umbraco standard

            var domainCache = new Mock<IDomainCache>();
            domainCache.Setup(x => x.GetAssigned(It.IsAny<int>(), false))
                .Returns((int contentId, bool includeWildcards) =>
                {
                    if (contentId != 9876) return Enumerable.Empty<Domain>();
                    return new[]
                    {
                        new Domain(2, "example.us", 9876, CultureInfo.GetCultureInfo("en-US"), false, true), //default
                        new Domain(3, "example.fr", 9876, CultureInfo.GetCultureInfo("fr-FR"), false, true)
                    };
                });

            var snapshot = Mock.Of<IPublishedSnapshot>(x => x.Content == publishedContentCache.Object && x.Domains == domainCache.Object);

            var snapshotService = new Mock<IPublishedSnapshotService>();
            snapshotService.Setup(x => x.CreatePublishedSnapshot(It.IsAny<string>()))
                .Returns(snapshot);

            var umbracoContext = GetUmbracoContext(currentUri, umbracoSettings: _umbracoSettings,
                urlProviders: new[] {
                    new DefaultUrlProvider(_umbracoSettings.RequestHandler, Logger, globalSettings.Object, new SiteDomainHelper())
                },
                globalSettings: globalSettings.Object,
                snapshotService: snapshotService.Object);


            var url = umbracoContext.UrlProvider.GetUrl(1234, "fr-FR");

            //the current uri is not the culture specific domain we want, so the result is an absolute path to the culture specific domain
            Assert.AreEqual("http://example.fr/home/test-fr/", url);
        }

        [Test]
        public void Get_Url_Relative_Or_Absolute()
        {
            var globalSettings = Mock.Get(TestObjects.GetGlobalSettings()); //this will modify the IGlobalSettings instance stored in the container
            globalSettings.Setup(x => x.UseDirectoryUrls).Returns(true);
            globalSettings.Setup(x => x.HideTopLevelNodeFromPath).Returns(false);
            SettingsForTests.ConfigureSettings(globalSettings.Object);


            var requestMock = Mock.Get(_umbracoSettings.RequestHandler);
            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);

            var umbracoContext = GetUmbracoContext("http://example.com/test", 1111, umbracoSettings: _umbracoSettings, urlProviders: new[]
            {
                new DefaultUrlProvider(_umbracoSettings.RequestHandler, Logger, globalSettings.Object, new SiteDomainHelper())
            }, globalSettings: globalSettings.Object);

            Assert.AreEqual("/home/sub1/custom-sub-1/", umbracoContext.UrlProvider.GetUrl(1177));

            requestMock.Setup(x => x.UseDomainPrefixes).Returns(true);
            Assert.AreEqual("http://example.com/home/sub1/custom-sub-1/", umbracoContext.UrlProvider.GetUrl(1177));

            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);
            umbracoContext.UrlProvider.Mode = UrlProviderMode.Absolute;
            Assert.AreEqual("http://example.com/home/sub1/custom-sub-1/", umbracoContext.UrlProvider.GetUrl(1177));
        }

        [Test]
        public void Get_Url_Unpublished()
        {
            var globalSettings = Mock.Get(TestObjects.GetGlobalSettings()); //this will modify the IGlobalSettings instance stored in the container
            globalSettings.Setup(x => x.UseDirectoryUrls).Returns(true);
            globalSettings.Setup(x => x.HideTopLevelNodeFromPath).Returns(false);
            SettingsForTests.ConfigureSettings(globalSettings.Object);

            var umbracoContext = GetUmbracoContext("http://example.com/test", 1111, urlProviders: new[]
            {
                new DefaultUrlProvider(_umbracoSettings.RequestHandler, Logger, globalSettings.Object, new SiteDomainHelper())
            }, globalSettings: globalSettings.Object);

            //mock the Umbraco settings that we need
            var requestMock = Mock.Get(_umbracoSettings.RequestHandler);
            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);

            Assert.AreEqual("#", umbracoContext.UrlProvider.GetUrl(999999));

            requestMock.Setup(x => x.UseDomainPrefixes).Returns(true);

            Assert.AreEqual("#", umbracoContext.UrlProvider.GetUrl(999999));

            requestMock.Setup(x => x.UseDomainPrefixes).Returns(false);

            umbracoContext.UrlProvider.Mode = UrlProviderMode.Absolute;

            Assert.AreEqual("#", umbracoContext.UrlProvider.GetUrl(999999));
        }
    }
}
