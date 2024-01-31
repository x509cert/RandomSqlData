# Define the path to your text file
$filePath = ".\last-names.txt"

# Read the content of the file
$content = Get-Content -Path $filePath

# Process each line
$updatedContent = $content | ForEach-Object {
    # Check if the line is not empty
    if ($_ -ne "") {
        # Capitalize the first letter and concatenate with the rest of the string
        $_.Substring(0,1).ToUpper() + $_.Substring(1)
    } else {
        # If the line is empty, just return it
        $_
    }
}

# Write the updated content back to the file
$updatedContent | Set-Content -Path $filePath

