using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using OpenRealEstate.Core;
using OpenRealEstate.Core.Residential;
using OpenRealEstate.FakeData;
using OpenRealEstate.Testing;
using OpenRealEstate.Transmorgrifiers.Core;
using OpenRealEstate.Transmorgrifiers.Json;
using Shouldly;
using Xunit;

namespace OpenRealEstate.Transmorgrifiers.RealEstateComAu.Tests
{
    public class ParseResidentialTests
    {
        private static ResidentialListing CreateAFakeEmptyResidentialListing(string id)
        {
            id.ShouldNotBeNullOrWhiteSpace();

            return new ResidentialListing
            {
                AgencyId = "XNWXNW",
                Id = id,
                CreatedOn = new DateTime(2009, 1, 1, 12, 30, 0),
                UpdatedOn = new DateTime(2009, 1, 1, 12, 30, 0)
            };
        }

        private static void AssertResidentialListing(ParsedResult result,
                                                     ResidentialListing expectedListing)
        {
            result.ShouldNotBeNull();
            result.Listings.Count.ShouldBe(1);
            result.Errors.Count.ShouldBe(0);
            result.UnhandledData.Count.ShouldBe(0);
            result.TransmorgrifierName.ShouldBe("REA");
            ResidentialListingAssertHelpers.AssertResidentialListing(result.Listings.First().Listing as ResidentialListing,
                                                                     expectedListing);
        }

        private static void AssertParsingError(ParsedResult result,
                                               ParsedError error)
        {
            result.ShouldNotBeNull();
            result.Listings.Count.ShouldBe(0);
            result.UnhandledData.Count.ShouldBe(0);

            error.ShouldNotBeNull();
            result.Errors.First().ExceptionMessage.ShouldBe(error.ExceptionMessage);
            result.Errors.First().InvalidData.ShouldNotBeNullOrWhiteSpace();
        }

        private const string FakeDataFolder = "Sample Data\\Residential\\";

        [Theory]
        [InlineData("REA-Residential-Current.xml")]
        [InlineData("REA-Segment-Residential-Current.xml")]
        [InlineData("REA-Residential-Current-WithAllFeatures.xml")]
        [InlineData("REA-Residential-Current-WithAStreetNumberAndASingleSubNumber.xml")]
        [InlineData("REA-Residential-Current-WithA4PointZeroZeroBedroomNumber.xml")]
        [InlineData("REA-Residential-Current-WithOptionalAuctionDateTimeText.xml")]
        public void GivenTheFileREAResidentialCurrent_Parse_ReturnsAResidentialAvailableListing(string fileName)
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            var reaXml = File.ReadAllText(FakeDataFolder + fileName);
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }
        
        [Theory]
        [InlineData("REA-Residential-Withdrawn.xml", "withdrawn", "Residential-Withdrawn-ABCD1234")]
        [InlineData("REA-Residential-OffMarket.xml", "offmarket", "Residential-OffMarket-ABCD1234")]
        [InlineData("REA-Residential-Deleted.xml", "deleted", "Residential-Deleted-ABCD1234")]
        public void GivenAnReaFileThatRepresentsARemovedListing_Parse_ReturnsAResidentialRemovedListing(string fileName,
                                                                                                        string sourceStatus,
                                                                                                        string id)
        {
            // Arrange.
            var expectedListing = CreateAFakeEmptyResidentialListing(id);
            expectedListing.StatusType = StatusType.Removed;
            expectedListing.SourceStatus = sourceStatus;

            var reaXml = File.ReadAllText(FakeDataFolder + fileName);
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Theory]
        [InlineData("REA-Residential-Current-WithBadInspectionTime.xml",
            "Inspection element has an invald Date/Time value. Element: <inspection> 12:00AM to 12:00AM</inspection>")]
        [InlineData("REA-Residential-Current-BadSalePrice.xml", "Failed to parse element: residential.price; value: '550000600000550000600000550000600000' into a decimal/money.")]
        [InlineData("REA-Residential-Sold-DisplayAttributeIsRange.xml",
            "Value 'range' is out of range. It should only be 0/1/yes/no.\r\nParameter name: value")]
        [InlineData("REA-Residential-Current-WithABadBedroomNumber.xml",
            "Failed to parse the value '4.5' into an int. Is it a valid number? Does it contain decimal point values?")]
        [InlineData("REA-Residential-Current-WithTooManyBedrooms.xml", "Failed to parse the value '3334' into a byte.")]
        [InlineData("REA-Residential-Current-WithBadModTimeInImagesAndFloorPlans.xml",
            "Invalid date/time trying to be parsed. Attempted the value: '2016-02-1112:50:05' but that format is invalid. Element/Attribute: <img modTime='..'/>")]
        public void GivenAnReaFileWithSomeTypeOfBadData_Parse_ReturnsAParsedError(string fileName,
                                                                                  string errorMessage)
        {
            // Arrange.
            var reaXml = File.ReadAllText(FakeDataFolder + fileName);
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            var error = new ParsedError(errorMessage, reaXml);
            AssertParsingError(result, error);
        }

        [Theory]
        [InlineData("REA-Residential-Current.xml", false, StatusType.Available)]
        [InlineData("REA-Residential-Current-WithFloorPlansMissing.xml", true, StatusType.Available)]
        [InlineData("REA-Residential-Sold.xml", false, StatusType.Sold)]
        public void
            GivenTheFileREAResidentialCurrent_ParseThenSerializeThenDeserialize_ReturnsAResidentialAvailableListing(string fileName,
                                                                                                                    bool isFloorPlansCleared,
                                                                                                                    StatusType statusType)
        {
            // Arrange.
            ResidentialListing expectedListing;
            if (statusType == StatusType.Available)
            {
                expectedListing = FakeListings.CreateAFakeResidentialListing();
            }
            else
            {
                expectedListing = CreateAFakeEmptyResidentialListing("Residential-Sold-ABCD1234");
                expectedListing.StatusType = StatusType.Sold;
                expectedListing.SourceStatus = "sold";
                expectedListing.Pricing = new SalePricing
                {
                    SoldOn = new DateTime(2009, 1, 10, 12, 30, 00),
                    SoldPrice = 580000M,
                    SoldPriceText = "$580,000"
                };
            }

            if (isFloorPlansCleared)
            {
                expectedListing.FloorPlans = new List<Media>();
            }
            var reaXml = File.ReadAllText(FakeDataFolder + fileName);
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Parse the xml, once for the first time.
            var tempResult = reaXmlTransmorgrifier.Parse(reaXml);
            var source = tempResult.Listings.First().Listing;
            var json = source.SerializeObject();

            // Act.
            var result = JsonConvertHelpers.DeserializeObject(json);

            // Assert.
            var listingResult = new ListingResult
            {
                Listing = result,
                SourceData = "blahblah"
            };
            var parsedResult = new ParsedResult
            {
                Listings = new List<ListingResult>
                {
                    listingResult
                },
                UnhandledData = new List<string>(),
                Errors = new List<ParsedError>(),
                TransmorgrifierName = "REA"
            };
            AssertResidentialListing(parsedResult, expectedListing);
        }

        [Theory]
        [InlineData("REA-Residential-Current-WithEnsuiteIsTrue.xml", 1)]
        [InlineData("REA-Residential-Current-WithEnsuiteIsFalse.xml", 0)]
        public void GivenTheFileREAResidentialCurrentWithEnsuiteIsTrue_Parse_ReturnsAResidentialAvailableListing(
            string filename,
            byte ensuiteCount)
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Features.Ensuites = ensuiteCount;
            var reaXml = File.ReadAllText(FakeDataFolder + filename);
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Theory]
        [InlineData("2016-10-05-0:00:00 ", "5/10/2016 12:00:00 AM")] // Notice the space at the end.
        [InlineData("2016-10-01-9:30:00", "1/10/2016 9:30:00 AM")] // Time is not in 24 hours. Is this 9pm or 9am?
        public void GivenTheFileREAResidentialCurrentWithPoorlyFormattedDateTime_Convert_ReturnsAResidentialListing(string bustedDateTime,
                                                                                                                    string expectedResult)
        {
            // Arrange.
            var reaXml =
                File.ReadAllText("Sample Data\\Residential\\REA-Residential-Current-WithAuctionDateTimePlaceholder.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            var updatedXml = reaXml.Replace("REPLACE-THIS-VALUE", bustedDateTime);

            // Act.
            var result = reaXmlTransmorgrifier.Parse(updatedXml);

            // Assert.
            result.ShouldNotBeNull();
            result.Listings.ShouldNotBeNull();
            result.UnhandledData.ShouldBeEmpty();
            result.Errors.ShouldBeEmpty();

            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-AU");
            ((ResidentialListing) result.Listings.First().Listing).AuctionOn.ToString().ShouldBe(expectedResult);
        }

        [Theory]
        [InlineData("T:18:30")] // '2016-10-01-9:30:00
        [InlineData("-0001-11-30 00:00:00")]
        [InlineData("2016-00-00")]
        [InlineData("0000-00-00-00:00")]
        [InlineData("0000-00-00")]
        [InlineData("0000-00-00T00:00")]
        [InlineData("T:")]
        [InlineData("- - -0:00:00 ")]
        public void GivenTheFileREAResidentialCurrentWithBadAuctionDateTime_Convert_RetrunsSomeInvalidData(string bustedDateTime)
        {
            // Arrange.
            var reaXml =
                File.ReadAllText("Sample Data\\Residential\\REA-Residential-Current-WithAuctionDateTimePlaceholder.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            var updatedXml = reaXml.Replace("REPLACE-THIS-VALUE", bustedDateTime);

            // Act.
            var result = reaXmlTransmorgrifier.Parse(updatedXml);

            // Assert.
            result.ShouldNotBeNull();
            result.Listings.ShouldBeEmpty();
            result.UnhandledData.ShouldBeEmpty();
            result.Errors.Count.ShouldBe(1);
            result.Errors.First().AgencyId.ShouldBe("XNWXNW");
            result.Errors.First().ListingId.ShouldBe("Residential-Current-ABCD1234");
        }

        [Fact]
        public void GivenAFileWithAtLeastOneDocument_Convert_ReturnsAListingWithADocument()
        {
            // Arramge.
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            var listingData = result.Listings.First();
            var documents = listingData.Listing.Documents;
            documents.Count.ShouldBe(1);
            var document = documents.First();
            document.Id.ShouldBe("aaaa1111");
            document.CreatedOn.ShouldNotBeNull();
            document.Order.ShouldBe(1);
            document.Tag.ShouldBe("statementOfInformation");
            document.Url.ShouldBeEmpty();
        }

        [Fact]
        public void GivenTheFileREAResidentialCurrentBedroomIsStudio_Parse_ReturnsAResidentialAvailableListing()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.PropertyType = PropertyType.Studio;
            expectedListing.Features.Bedrooms = 0;
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-BedroomIsStudio.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void GivenTheFileREAResidentialCurrentMinimum_Parse_ReturnsAResidentialAvailableListing()
        {
            // Arrange.
            var expectedListing = CreateAFakeEmptyResidentialListing("Residential-Current-ABCD1234");
            expectedListing.StatusType = StatusType.Available;
            expectedListing.SourceStatus = "current";
            expectedListing.Address = new Address
            {
                StreetNumber = "2/39",
                Street = "Main Road",
                Suburb = "RICHMOND",
                State = "Victoria",
                Postcode = "3121",
                CountryIsoCode = "AU"
            };
            expectedListing.Address.DisplayAddress = expectedListing.Address.ToString();
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-Minimum.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void GivenTheFileREAResidentialCurrentMinimumAndAnExistingListing_Parse_ReturnsAResidentialAvailableListing()
        {
            // Arrange.
            var source = FakeListings.CreateAFakeResidentialListing();
            var destination = FakeListings.CreateAFakeResidentialListing();

            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-Minimum.xml");

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml, source);

            // Assert.
            AssertResidentialListing(result, destination);
        }

        [Fact]
        public void GivenTheFileREAResidentialCurrentWithASingleAgentName_Parse_ReturnsAResidentialAvailableListing()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Agents[0].Communications = new List<Communication>(); // Remove all communications for this agent.
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithASingleAgentName.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void
            GivenTheFileREAResidentialCurrentWithAStreetNumberAndASingleSubNumberWithACustomDelimeter_Parse_ReturnsAResidentialAvailableListing
            ()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Address.StreetNumber = "2-39";
            expectedListing.Address.DisplayAddress = expectedListing.Address.ToString();
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithAStreetNumberAndASingleSubNumber.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier
            {
                AddressDelimeter = "-"
            };

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void
            GivenTheFileREAResidentialCurrentWithAStreetNumberAndASubNumber_Parse_ReturnsAResidentialAvailableListing
            ()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Address.StreetNumber = "2/77a 39";
            expectedListing.Address.DisplayAddress = expectedListing.Address.ToString();

            var reaXml =
                File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithAStreetNumberAndASubNumber.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void GivenTheFileREAResidentialCurrentWithDuplicateAgents_Parse_ReturnsAResidentialAvailableListing()
        {
            // Arrange.
            var agents = new List<Agent>
            {
                new Agent
                {
                    Name = "Princess Leia",
                    Order = 1,
                    Communications = new List<Communication>
                    {
                        new Communication
                        {
                            CommunicationType = CommunicationType.Email,
                            Details = "ImAPrincess@rebelalliance.com"
                        },
                        new Communication
                        {
                            CommunicationType = CommunicationType.Mobile,
                            Details = "1234 1234"
                        }
                    }
                },
                new Agent
                {
                    Name = "Han Solo",
                    Order = 2,
                    Communications = new List<Communication>
                    {
                        new Communication
                        {
                            CommunicationType = CommunicationType.Email,
                            Details = "IShotFirst@rebelalliance.com"
                        },
                        new Communication
                        {
                            CommunicationType = CommunicationType.Mobile,
                            Details = "0987 0987"
                        }
                    }
                }
            };
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Agents = agents;
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithDuplicateAgents.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }


        [Fact]
        public void
            GivenTheFileREAResidentialCurrentWithEmptyImagesAndFloorplans_Parse_ReturnsAResidentialAvailableListing()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Images = new List<Media>();
            expectedListing.FloorPlans = new List<Media>();
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithEmptyImagesAndFloorplans.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void GivenTheFileREAResidentialCurrentWithLocalFilesForImages_Parse_ReturnsAListing()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Images[0].Url = "imageM.jpg";
            expectedListing.Images[1].Url = "imageA.jpg";
            expectedListing.FloorPlans[0].Url = "floorplan1.gif";
            expectedListing.FloorPlans[1].Url = "floorplan2.gif";
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithLocalFilesForImages.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void
            GivenTheFileREAResidentialCurrentWithNoModTimeInImagesAndFloorPlans_Parse_ReturnsAResidentialAvailableListing
            ()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Images.ToList().ForEach(x => x.CreatedOn = null);
            expectedListing.FloorPlans.ToList().ForEach(x => x.CreatedOn = null);
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithNoModTimeInImagesAndFloorPlans.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void
            GivenTheFileREAResidentialCurrentWithNoStreetNumberButASubNumber_Parse_ReturnsAResidentialAvailableListing
            ()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Address.StreetNumber = "2/77a";
            expectedListing.Address.DisplayAddress = expectedListing.Address.ToString();

            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithNoStreetNumberButASubNumber.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Theory]
        [InlineData("REA-Residential-Current-WithPriceAndDisplayNoAndAPriceView.xml", true, null)] // Set the default and then use that.
        [InlineData("REA-Residential-Current-WithPriceAndDisplayNoAndAPriceView.xml", true, "aaaaa")] // Set the default and then use that.
        [InlineData("REA-Residential-Current-WithPriceAndDisplayNoAndAPriceView.xml", false, null)] // Will use the default Sale price value.
        [InlineData("REA-Residential-Current-WithPriceAndDisplayNoAndNoPriceView.xml", true, null)] // Set the default and then use that.
        [InlineData("REA-Residential-Current-WithPriceAndDisplayNoAndNoPriceView.xml", true, "aaaaa")] // Set the default and then use that.
        [InlineData("REA-Residential-Current-WithPriceAndDisplayNoAndNoPriceView.xml", false, null)] // Will use the default Sale price value.
        public void GivenTheFileREAResidentialCurrentSomeVariousDisplayPriceOptions_Parse_ReturnsAListing(
            string fileName,
            bool isDefaultSalePriceSet,
            string expectedSalePriceText)
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Pricing.SalePriceText = expectedSalePriceText;
            var reaXml = File.ReadAllText($"{FakeDataFolder}{fileName}");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();
            if (isDefaultSalePriceSet)
            {
                reaXmlTransmorgrifier.DefaultSalePriceTextIfMissing = expectedSalePriceText;
            }

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void GivenTheFileREAResidentialCurrentWithPriceAndDisplayYesButNoPriceView_Parse_ReturnsAListing()
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Pricing.SalePriceText = "$500,000";
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-WithPriceAndDisplayYesButNoPriceView.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Fact]
        public void GivenTheFileREAResidentialSold_Parse_ReturnsAResidentialSoldListing()
        {
            // Arrange.
            var expectedListing = CreateAFakeEmptyResidentialListing("Residential-Sold-ABCD1234");
            expectedListing.StatusType = StatusType.Sold;
            expectedListing.SourceStatus = "sold";
            expectedListing.Pricing = new SalePricing
            {
                SoldOn = new DateTime(2009, 1, 10, 12, 30, 00),
                SoldPrice = 580000M,
                SoldPriceText = "$580,000"
            };
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Sold.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Theory]
        [InlineData(true, null)] // Set the default and then use that.
        [InlineData(true, "aaaaa")] // Set the default and then use that.
        [InlineData(false, null)] // Will use the default Sale price value.
        public void GivenTheFileREAResidentialSoldWithDisplayPriceIsNo_Parse_ReturnsAResidentialSoldListingWithNoSoldPriceText(bool isDefaultSalePriceSet,
                                                                                                                               string expectedSoldPriceText)
        {
            // Arrange.
            var expectedListing = CreateAFakeEmptyResidentialListing("Residential-Sold-ABCD1234");
            expectedListing.StatusType = StatusType.Sold;
            expectedListing.SourceStatus = "Sold";
            expectedListing.Pricing = new SalePricing
            {
                SoldOn = new DateTime(2009, 1, 10, 12, 30, 00),
                SoldPrice = 580000M,
                SoldPriceText = expectedSoldPriceText
            };

            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Sold-DisplayPriceIsNo.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();
            if (isDefaultSalePriceSet)
            {
                reaXmlTransmorgrifier.DefaultSoldPriceTextIfMissing = expectedSoldPriceText;
            }

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        // NOTE: no display attribute for the sold-data element means a 'yes', please show the value.
        [Fact]
        public void GivenTheFileREAResidentialSoldWithMissingDisplayPrice_Parse_ReturnsAResidentialSoldListing()
        {
            // Arrange.
            var expectedListing = CreateAFakeEmptyResidentialListing("Residential-Sold-ABCD1234");
            expectedListing.StatusType = StatusType.Sold;
            expectedListing.SourceStatus = "sold";
            expectedListing.Pricing = new SalePricing
            {
                SoldOn = new DateTime(2009, 1, 10, 12, 30, 00),
                SoldPrice = 580000M,
                SoldPriceText = "$580,000"
            };

            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Sold-MissingDisplayPrice.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }

        [Theory]
        [InlineData("REA-Residential-Current.xml", "2/39 Main Road, RICHMOND, Victoria 3121")] // Display == true/yes.
        [InlineData("REA-Residential-Current-AddressDisplayIsNo.xml", "RICHMOND, Victoria 3121")] // Display == false/no.
        public void GivenTheFileREAResidentialWithSomeAddressDisplayValues_Parse_ReturnsAResidentialSoldListing(string fileName,
                                                                                                                string expectedDisplayAddress)
        {
            // Arrange.
            var expectedListing = FakeListings.CreateAFakeResidentialListing();
            expectedListing.Address.DisplayAddress = expectedDisplayAddress;
            var reaXml = File.ReadAllText(FakeDataFolder + fileName);
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            AssertResidentialListing(result, expectedListing);
        }
        
        [Fact]
        public void GivenTheFileREAResidentialCurrentWithMoreThan26Images_Parse_ReturnsAResidentialAvailableListing()
        {
            // Arrange.
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-ManyImages.xml");
            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml);

            // Assert.
            var listing = result.Listings.First().Listing;
            listing.Images.Count.ShouldBe(31);
            listing.Images.First().Id.ShouldBe("m");
            listing.Images.First().Url.EndsWith("m.jpg");
            listing.Images[1].Id.ShouldBe("a");
            listing.Images[1].Url.EndsWith("a.jpg");
            listing.Images.Last().Id.ShouldBe("ae");
            listing.Images.Last().Url.EndsWith("ae.jpg");
        }

        [Fact]
        public void GivenAnExistingListingAndTheResultListingChangesSomething_Parse_ReturnsANewListingWithTheSourceListingWasntChanged()
        {
            // Arrange.
            var source = FakeListings.CreateAFakeResidentialListing();
            var destination = FakeListings.CreateAFakeResidentialListing();

            var reaXmlTransmorgrifier = new ReaXmlTransmorgrifier();
            var reaXml = File.ReadAllText(FakeDataFolder + "REA-Residential-Current-Minimum.xml");

            // Act.
            var result = reaXmlTransmorgrifier.Parse(reaXml, source);

            // Assert.

            // Change something on the result which shouldn't effect the original source.
            var newListing = result.Listings.First().Listing;
            newListing.Description.ShouldBe(source.Description); // Right now, both are the same.
            newListing.Description = DateTime.UtcNow.ToString(); // Change.
            newListing.Description.ShouldNotBe(source.Description); // Both should now be different.
        }
    }
}
