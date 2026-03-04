using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace FishLens_App
{
    public partial class History
    {
        #region Helper Methods - Filters

        // **************************************************
        // Function: UpdateFiltersFromUI
        // Description: Updates internal filter state from UI control values
        private void UpdateFiltersFromUI()
        {
            if (startDatePicker?.SelectedDate != null)
                _filterStartDate = startDatePicker.SelectedDate.Value;
            else
                _filterStartDate = null;

            if (endDatePicker?.SelectedDate != null)
                _filterEndDate = endDatePicker.SelectedDate.Value;
            else
                _filterEndDate = null;

            if (speciesFilter?.SelectedItem != null)
            {
                var selectedItem = (ComboBoxItem)speciesFilter.SelectedItem;
                string content = selectedItem.Content.ToString();
                _filterSpecies = content.Contains("All") ? "All" : content;
            }

            if (directionFilter?.SelectedItem != null)
            {
                var selectedItem = (ComboBoxItem)directionFilter.SelectedItem;
                string content = selectedItem.Content.ToString();
                _filterDirection = content.Contains("All") ? "All" : content;
            }

            if (cameraFilter?.SelectedItem != null)
            {
                var selectedItem = (ComboBoxItem)cameraFilter.SelectedItem;
                string content = selectedItem.Content.ToString();
                _filterCamera = content.Contains("All") ? "All" : content;
            }
        }

        // **************************************************
        // Function: ApplyFilters
        // Description: Filters CSV data based on current filter criteria including date range
        private string[] ApplyFilters(string[] csvLines)
        {
            var filtered = new List<string>();

            foreach (string line in csvLines)
            {
                string[] columns = line.Split(',');
                if (columns.Length == 0) continue;

                string videoName = columns.Length > 0 ? columns[0].Trim() : string.Empty;
                string species = columns.Length > 9 ? columns[9].Trim() : (columns.Length > 3 ? columns[3].Trim() : string.Empty);
                string direction = columns.Length > 8 ? columns[8].Trim() : (columns.Length > 7 ? columns[7].Trim() : string.Empty);

                double? avgConf = null;
                if (columns.Length > 7)
                {
                    var raw = columns[7].Trim().TrimEnd('%');
                    if (double.TryParse(raw, out var d))
                    {
                        if (d > 1) d = d / 100.0;
                        avgConf = d;
                    }
                }

                DateTime? timestamp = null;
                if (columns.Length > 11)
                {
                    var datePart = columns[11].Trim();
                    var timePart = columns.Length > 12 ? columns[12].Trim() : string.Empty;
                    if (!string.IsNullOrEmpty(timePart))
                    {
                        if (DateTime.TryParse($"{datePart} {timePart}", out DateTime ts)) timestamp = ts;
                    }
                    else
                    {
                        if (DateTime.TryParse(datePart, out DateTime ts2)) timestamp = ts2;
                    }
                }

                string location = ExtractLocationFromVideo(videoName);

                if (!PassesSpeciesFilter(species)) continue;
                if (!PassesDirectionFilter(direction)) continue;
                if (!PassesConfidenceFilter(avgConf)) continue;
                if (!PassesDateFilter(timestamp)) continue;
                if (!PassesCameraFilter(location)) continue;

                filtered.Add(line);
            }

            return filtered.ToArray();
        }

        // **************************************************
        // Function: PassesSpeciesFilter
        // Description: Checks if a species value passes the current species filter
        private bool PassesSpeciesFilter(string species)
        {
            return _filterSpecies == "All" || species.Equals(_filterSpecies, StringComparison.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: PassesDirectionFilter
        // Description: Checks if a direction value passes the current direction filter
        private bool PassesDirectionFilter(string direction)
        {
            return _filterDirection == "All" || direction.Contains(_filterDirection, StringComparison.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: PassesCameraFilter
        // Description: Checks if a location value passes the current camera/location filter
        private bool PassesCameraFilter(string location)
        {
            return _filterCamera == "All" || location.Equals(_filterCamera, StringComparison.OrdinalIgnoreCase);
        }

        // **************************************************
        // Function: PassesConfidenceFilter
        // Description: Checks if a confidence value passes the minimum confidence threshold
        private bool PassesConfidenceFilter(double? avgConfidence)
        {
            if (_filterMinConfidence <= 0.0) return true;
            if (!avgConfidence.HasValue) return false;
            return avgConfidence.Value >= _filterMinConfidence;
        }

        // **************************************************
        // Function: PassesDateFilter
        // Description: Checks if a detection date falls within the selected date range
        private bool PassesDateFilter(DateTime? detectionTimestamp)
        {
            if (!_filterStartDate.HasValue && !_filterEndDate.HasValue)
                return true;

            if (!detectionTimestamp.HasValue)
                return false;

            var detectionDate = detectionTimestamp.Value.Date;

            if (_filterStartDate.HasValue && detectionDate < _filterStartDate.Value.Date)
                return false;

            if (_filterEndDate.HasValue && detectionDate > _filterEndDate.Value.Date)
                return false;

            return true;
        }

        #endregion
    }
}