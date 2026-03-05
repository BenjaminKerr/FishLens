using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace FishLens_App
{
    public partial class History
    {

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

                string videoName = columns.Length > 2 ? columns[2].Trim() : string.Empty;
                string species = columns.Length > 11 ? columns[11].Trim() : string.Empty;
                string direction = columns.Length > 10 ? columns[10].Trim() : string.Empty;

                double? avgConf = null;
                if (columns.Length > 9)
                {
                    var raw = columns[9].Trim().TrimEnd('%');
                    if (double.TryParse(raw, out var d))
                    {
                        if (d > 1) d = d / 100.0;
                        avgConf = d;
                    }
                }

                DateTime? timestamp = null;
                if (columns.Length > 0)
                {
                    var datePart = columns[0].Trim();
                    var timePart = columns.Length > 1 ? columns[1].Trim() : string.Empty;
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

        // **************************************************
        // Function: PopulateFilterDropdowns
        // Description: Reads the CSV and fills filter dropdowns with actual values
        private void PopulateFilterDropdowns()
        {
            try
            {
                string csvPath = _pathResolver.ResolveCsvScriptPath();
                if (!File.Exists(csvPath)) return;

                string[] allLines = File.ReadAllLines(csvPath);
                if (allLines.Length <= 1) return;

                var species = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var directions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string line in allLines.Skip(1))
                {
                    string[] col = line.Split(',');

                    if (col.Length > 11)
                    {
                        string s = col[11].Trim();
                        if (!string.IsNullOrWhiteSpace(s))
                            species.Add(s);
                    }

                    if (col.Length > 10)
                    {
                        string d = col[10].Trim();
                        if (!string.IsNullOrWhiteSpace(d))
                            directions.Add(d);
                    }
                }

                // Species dropdown
                foreach (var s in species.OrderBy(x => x))
                    speciesFilter.Items.Add(new ComboBoxItem { Content = s });

                // Direction dropdown — only add values not already hardcoded
                foreach (var d in directions.OrderBy(x => x))
                {
                    bool alreadyPresent = directionFilter.Items
                        .OfType<ComboBoxItem>()
                        .Any(item => item.Content?.ToString().Equals(d, StringComparison.OrdinalIgnoreCase) == true);

                    if (!alreadyPresent)
                        directionFilter.Items.Add(new ComboBoxItem { Content = d });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error populating filter dropdowns");
            }
        }
    }
}