using System;
using System.Collections.Generic;

namespace endfield_player_position_display.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new List<Action>
            {
                TokenFileReaderTests.ReadTokenReadsTrimmedTokenFromBaseDirectory,
                TokenFileReaderTests.ReadTokensKeepsDuplicatesWhenRequested,
                TokenFileReaderTests.ReadTokensRemovesDuplicatesWhenRequested,
                TokenFileReaderTests.ReadTokenThrowsChineseErrorWhenFileMissing,
                TokenFileReaderTests.ReadTokenThrowsChineseErrorWhenFileBlank,
                TokenFileReaderTests.HasAnyTokenReturnsFalseWhenMissingOrBlank,
                TokenFileReaderTests.AppendTokenAddsLineWithoutOverwritingExistingTokens,
                TokenFileReaderTests.RemoveTokenAtRemovesOnlySelectedTokenLine,
                TokenFileReaderTests.MaskTokenKeepsOnlyShortEdges,
                SklandSignerTests.CreateHeaderJsonUsesAcceptedClientHeaderValues,
                SklandSignerTests.CreateSignReturnsMd5OfHmacSha256Hex,
                SklandSignerTests.CreateSignIncludesQueryOrBodySegment,
                SklandApiRequestTests.GrantAndGenerateCredentialUseCurrentSklandEndpoints,
                SklandApiParsingTests.CreateSignedRequestTimestampUsesServerAcceptedClockSkew,
                SklandApiParsingTests.CreateSignedRequestTimestampAppliesNetworkTimeOffset,
                NetworkTimeServiceTests.CalculateOffsetUsesNetworkDateMinusLocalDate,
                SklandApiParsingTests.ParseRoleBindingExtractsFirstEndfieldDefaultRole,
                SklandApiParsingTests.ParseRoleBindingsExtractsNicknameAndChannelName,
                SklandApiParsingTests.ParseRoleBindingThrowsChineseErrorWhenRoleMissing,
                SklandApiParsingTests.ParseWebSocketTokenExtractsDataToken,
                SklandApiParsingTests.ParseZiplineMarksFiltersSupportedTemplates,
                SklandApiParsingTests.ParseZiplineMarksThrowsChineseErrorForBadResponse,
                ScanLoginTests.ParseScanLoginSessionExtractsScanIdAndUrl,
                ScanLoginTests.ParseScanStatusMapsPendingScannedAndConfirmed,
                ScanLoginTests.ParseTokenByScanCodeExtractsToken,
                ScanLoginTests.ParsePhonePasswordLoginExtractsToken,
                ScanLoginTests.ParseSendPhoneCodeAcceptsOkResponse,
                ScanLoginTests.ParseSendPhoneCodeThrowsReturnedMessageForError,
                ScanLoginTests.ParsePhoneCodeLoginExtractsToken,
                ScanLoginTests.TokenFileWriterWritesUtf8TokenTxt,
                CoordinateFormatterTests.FormatPadsIntegerPartAndKeepsFiveFractionDigits,
                PositionWebSocketMessageTests.ParseMessageExtractsPositionFromType1012,
                PositionWebSocketMessageTests.ParseMessageExtractsRemoteCloseMessageFromType6,
                PositionWebSocketMessageTests.ParseMessageExtractsMapIdFromType1012,
                PositionWebSocketMessageTests.ParseMessageAllowsMissingMapId,
                PositionWebSocketMessageTests.ParseMessageUsesChineseErrorForInvalidPayload,
                ZiplineMatcherTests.FindNearestMatchesBottomLeftAsNorth,
                ZiplineMatcherTests.FindNearestMatchesBottomRightAsWest,
                ZiplineMatcherTests.FindNearestMatchesTopRightAsSouth,
                ZiplineMatcherTests.FindNearestMatchesTopLeftAsEast,
                ZiplineMatcherTests.FindNearestReturnsNoMatchBeyondThreeMeters,
                ZiplineMatcherTests.FindNearestChoosesClosestCandidate,
                ZiplineMatcherTests.FormatsCopyValues,
                ClipboardTextServiceTests.TrySetTextSetsDataObjectWithoutFlushAndSchedulesFlush,
                ClipboardTextServiceTests.TrySetTextIgnoresAsyncFlushFailure,
                ClipboardTextServiceTests.TrySetTextRetriesWhenClipboardIsBusy,
                ClipboardTextServiceTests.TrySetTextReturnsErrorWhenClipboardStaysBusy,
                DpiCoordinateConverterTests.FromDevicePixelsConvertsRectToDips,
                FollowWindowPlacementTests.CalculatePlacesAllEightDirections,
                FollowWindowPlacementTests.CalculateAppliesDirectionalOffsetsTowardInside,
                FollowWindowPlacementTests.CalculateAllowsNegativeCenterAxisOffsets,
                PositionCaptureRecorderTests.RecorderWritesUtf8BomCsvWithMovementMetrics,
                SeamMotionAnalyzerTests.AnalyzeIgnoresPauseAndUsesRecentSegmentSpeed,
                SeamMotionAnalyzerTests.BuildReportUsesFirstLastSamplesAndKeepsMovingSegments,
                SeamMotionAnalyzerTests.BuildReportKeepsSegmentsContinuousAfterXzBreaks,
                SeamMotionAnalyzerTests.BuildReportKeepsSegmentsContinuousAfterManualBreaks,
                ZiplineMotionAnalyzerTests.AnalyzeDetectsStopPointsAndTurnIntersections,
                ZiplineMotionAnalyzerTests.AnalyzeInfersZiplineRangeWithoutManualStartOrEnd,
                ZiplineMotionAnalyzerTests.AnalyzeRealCaptureFindsTenZiplinePoints,
                ZiplineMotionAnalyzerTests.AnalyzeNewRealCapturesUsesExpectedPointCounts,
                ZiplineCollectionExporterTests.ExportMarksJsonUsesNewFormatWithBidirectionalConnections,
                ZiplineCollectionExporterTests.ExportRoutesJsonGroupsConnectedMarks,
                ZiplineCollectionExporterTests.ExportRoutesJsonSplitsWhenStopDoesNotConnectToPrevious,
                ZiplineRealtimeDetectorTests.DetectorConfirmsStablePositionNearMarkWithHeightOffset,
                ZiplineRealtimeDetectorTests.DetectorRejectsGroundPositionNearMarkWithWrongHeight,
                ZiplineRealtimeDetectorTests.DetectorDoesNotRepeatUntilLeavingPreviousMark,
                ZiplineRealtimeDetectorTests.DetectorReplaysLatestCaptureAndFindsFourStops,
                ZiplineRealtimeDetectorTests.DetectorReplaysCaptureWithFastConfirm,
                ZiplineRealtimeDetectorTests.DetectorReplaysCapture164946AndSplitsGroundTransition
            };

            int passed = 0;
            foreach (Action test in tests)
            {
                try
                {
                    test();
                    Console.WriteLine("PASS " + test.Method.DeclaringType.Name + "." + test.Method.Name);
                    passed++;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("FAIL " + test.Method.DeclaringType.Name + "." + test.Method.Name);
                    Console.Error.WriteLine(ex);
                    return 1;
                }
            }

            Console.WriteLine(passed + " tests passed.");
            return 0;
        }
    }
}
