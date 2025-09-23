using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SR.AnalogGain.Tests
{
    public class SwitchParameterTests
    {
        // ---- helpers (no reflection, no dynamic, no lambdas on dynamic) ----
        private static int FindIndexByTitle(AnalogGainModel model, string contains)
        {
            for (int i = 0; i < model.LocalParameterCount; i++)
            {
                var p = model.GetLocalParameter(i);
                if (p?.Title is string t &&
                    t.Contains(contains, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int MustFindIndexByTitle(AnalogGainModel model, string contains)
        {
            int idx = FindIndexByTitle(model, contains);
            Assert.True(idx >= 0, $"Parameter with title containing '{contains}' not found.");
            return idx;
        }

        // ---------------------------- tests ----------------------------

        [Fact]
        public void Model_ShouldExpose_AllSwitches()
        {
            var model = new AnalogGainModel();

            Assert.True(FindIndexByTitle(model, "LO-Z") >= 0);
            Assert.True(FindIndexByTitle(model, "PAD") >= 0);
            Assert.True(FindIndexByTitle(model, "PHASE") >= 0);
            Assert.True(FindIndexByTitle(model, "HPF") >= 0);
        }

        [Theory]
        [InlineData("LO-Z")]
        [InlineData("PAD")]
        [InlineData("PHASE")]
        [InlineData("HPF")]
        public void Switch_Defaults_ShouldBe_Off(string key)
        {
            var model = new AnalogGainModel();
            var idx = MustFindIndexByTitle(model, key);
            var p = model.GetLocalParameter(idx);

            Assert.Equal(0.0, p.NormalizedValue, 6);
        }

        [Theory]
        [InlineData("LO-Z")]
        [InlineData("PAD")]
        [InlineData("PHASE")]
        [InlineData("HPF")]
        public void Switch_Should_Accept_On_And_Off(string key)
        {
            var model = new AnalogGainModel();
            var idx = MustFindIndexByTitle(model, key);
            var p = model.GetLocalParameter(idx);

            p.NormalizedValue = 0.0;
            Assert.Equal(0.0, p.NormalizedValue, 6);

            p.NormalizedValue = 1.0;
            Assert.Equal(1.0, p.NormalizedValue, 6);

            p.NormalizedValue = 0.0;
            Assert.Equal(0.0, p.NormalizedValue, 6);
        }

        [Fact]
        public void LozSwitch_ShouldHave_Reasonable_Title()
        {
            var model = new AnalogGainModel();
            var idx = MustFindIndexByTitle(model, "LO-Z");
            var p = model.GetLocalParameter(idx);

            Assert.Contains("LO", p.Title, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Z", p.Title, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PadSwitch_ShouldHave_Reasonable_Title()
        {
            var model = new AnalogGainModel();
            var idx = MustFindIndexByTitle(model, "PAD");
            var p = model.GetLocalParameter(idx);

            Assert.Contains("PAD", p.Title, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PhaseSwitch_ShouldHave_Reasonable_Title()
        {
            var model = new AnalogGainModel();
            var idx = MustFindIndexByTitle(model, "PHASE");
            var p = model.GetLocalParameter(idx);

            Assert.True(p.Title.Contains("PHASE", StringComparison.OrdinalIgnoreCase) ||
                        p.Title.Contains("Ø", StringComparison.OrdinalIgnoreCase),
                        $"Unexpected phase title: '{p.Title}'");
        }

        [Fact]
        public void HpfSwitch_ShouldHave_Reasonable_Title()
        {
            var model = new AnalogGainModel();
            var idx = MustFindIndexByTitle(model, "HPF");
            var p = model.GetLocalParameter(idx);

            Assert.True(p.Title.Contains("HPF", StringComparison.OrdinalIgnoreCase) ||
                        p.Title.Contains("High", StringComparison.OrdinalIgnoreCase),
                        $"Unexpected HPF title: '{p.Title}'");
        }

        [Fact]
        public void PadSwitch_Id_ShouldBe_40()
        {
            var model = new AnalogGainModel();
            var idx = MustFindIndexByTitle(model, "PAD");
            var p = model.GetLocalParameter(idx);

            Assert.Equal(40, p.Id.Value);
        }

        [Fact]
        public void Switches_ShouldHave_UniqueIds()
        {
            var model = new AnalogGainModel();
            var keys = new[] { "LO-Z", "PAD", "PHASE", "HPF" };

            var ids = new List<int>();
            foreach (var key in keys)
            {
                var idx = MustFindIndexByTitle(model, key);
                var p = model.GetLocalParameter(idx);
                ids.Add(p.Id.Value);
            }

            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void Switches_Should_Clamp_To_01()
        {
            var model = new AnalogGainModel();

            foreach (var key in new[] { "LO-Z", "PAD", "PHASE", "HPF" })
            {
                var idx = MustFindIndexByTitle(model, key);
                var p = model.GetLocalParameter(idx);

                p.NormalizedValue = -1.0;
                Assert.InRange(p.NormalizedValue, 0.0, 1.0);

                p.NormalizedValue = 2.0;
                Assert.InRange(p.NormalizedValue, 0.0, 1.0);

                p.NormalizedValue = 1.0;
                Assert.Equal(1.0, p.NormalizedValue, 6);
            }
        }
    }
}
