﻿#region "copyright"

/*
    Copyright (c) 2024 Dale Ghent <daleg@elemental.org>

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/
*/

#endregion "copyright"

using DaleGhent.NINA.PlaneWaveTools.Enum;
using DaleGhent.NINA.PlaneWaveTools.Utility;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DaleGhent.NINA.PlaneWaveTools.HeaterControl {

    [ExportMetadata("Name", "Heater Control (PWI4)")]
    [ExportMetadata("Description", "Sets PlaneWave heater state via PWI4")]
    [ExportMetadata("Icon", "HeatControl_SVG")]
    [ExportMetadata("Category", "PlaneWave Tools")]
    [Export(typeof(ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public class HeaterControl : SequenceItem, IValidatable, INotifyPropertyChanged {
        private HeaterType heater = HeaterType.M1Heater;
        private short heaterPower = 0;

        [ImportingConstructor]
        public HeaterControl() {
            Pwi4IpAddress = Properties.Settings.Default.Pwi4IpAddress;
            Pwi4Port = Properties.Settings.Default.Pwi4Port;

            Properties.Settings.Default.PropertyChanged += SettingsChanged;
        }

        [JsonProperty]
        public HeaterType Heater {
            get => heater;
            set {
                if (value != heater) {
                    heater = value;
                    RaisePropertyChanged();
                    RaisePropertyChanged(nameof(HeaterByName));
                }
            }
        }

        [JsonProperty]
        public short HeaterPower {
            get => heaterPower;
            set {
                if (value != heaterPower) {
                    var dutyCycle = value;

                    if (dutyCycle < 0) {
                        dutyCycle = 0;
                    }

                    if (dutyCycle > 100) {
                        dutyCycle = 100;
                    }

                    heaterPower = dutyCycle;
                    RaisePropertyChanged();
                }
            }
        }

        public string HeaterByName => Heater.GetDescriptionAttr();

        private HeaterControl(HeaterControl copyMe) : this() {
            CopyMetaData(copyMe);
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken ct) {
            var heaterId = Heater switch {
                HeaterType.M1Heater => "m1",
                HeaterType.M2Heater => "m2",
                HeaterType.M3Heater => "m3",
                _ => throw new SequenceEntityFailedException($"Unknown heater type \"{HeaterByName}\""),
            };

            string url = $"/heaters/set?role={heaterId}&power={HeaterPower}";

            var response = await Utilities.HttpRequestAsync(Pwi4IpAddress, Pwi4Port, url, HttpMethod.Get, string.Empty, ct);

            if (response.StatusCode != System.Net.HttpStatusCode.OK) {
                var error = await response.Content.ReadAsStringAsync(ct);
                throw new SequenceEntityFailedException($"Could not set heater \"{HeaterByName}\" to {HeaterPower}%: {error.Trim()}");
            }

            return;
        }

        public override object Clone() {
            return new HeaterControl(this) {
                Heater = Heater,
                HeaterPower = HeaterPower,
            };
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {Name}, Heater: {HeaterByName}, HeaterPower: {HeaterPower}";
        }

        public IList<string> Issues { get; set; } = new ObservableCollection<string>();

        public bool Validate() {
            var i = new List<string>();
            var status = new Dictionary<string, string>();

            Task.Run(async () => {
                try {
                    status = await Utilities.Pwi4GetStatus(Pwi4IpAddress, Pwi4Port, CancellationToken.None);
                } catch (HttpRequestException) {
                    i.Add("Could not communicate with PWI4");
                } catch (Exception ex) {
                    i.Add($"{ex.Message}");
                }
            }).Wait();

            if (i.Count > 0) {
                goto end;
            }

            if (!status.ContainsKey("mount.is_connected")) {
                i.Add("Unable to determine mount connection status");
                goto end;
            }

            if (!Utilities.Pwi4BoolStringToBoolean(status["mount.is_connected"])) {
                i.Add("PWI4 is not connected to the mount");
                goto end;
            }

        end:
            if (i != Issues) {
                Issues = i;
                RaisePropertyChanged(nameof(Issues));
            }

            return i.Count == 0;
        }

        private string Pwi4IpAddress { get; set; }
        private ushort Pwi4Port { get; set; }

        private void SettingsChanged(object sender, PropertyChangedEventArgs e) {
            switch (e.PropertyName) {
                case nameof(Pwi4IpAddress):
                    Pwi4IpAddress = Properties.Settings.Default.Pwi4IpAddress;
                    break;

                case nameof(Pwi4Port):
                    Pwi4Port = Properties.Settings.Default.Pwi4Port;
                    break;
            }
        }
    }
}