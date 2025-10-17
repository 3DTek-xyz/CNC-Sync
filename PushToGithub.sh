#!/bin/bash

# PushToGithub.sh - Script to properly push changes to GitHub with new version tag
# Usage: ./PushToGithub.sh [version] [commit-message]
# Example: ./PushToGithub.sh "1.0.61" "Fix bug in folder processing"

set -e  # Exit on any error

# Check if version is provided
if [ $# -lt 2 ]; then
    echo "Usage: $0 <version> <commit-message>"
    echo "Example: $0 '1.0.61' 'Fix bug in folder processing'"
    exit 1
fi

VERSION="$1"
COMMIT_MESSAGE="$2"

echo "=================================================="
echo "GitHub Push Script - Version v$VERSION"
echo "=================================================="

# Step 1: Pull latest changes first to avoid conflicts
echo "Step 1: Pulling latest changes from GitHub..."
git fetch origin
git pull origin main --no-edit

# Step 2: Add all changes
echo "Step 2: Adding all changes to staging..."
git add .

# Check if there are any changes to commit
if git diff --staged --quiet; then
    echo "No changes to commit. Exiting."
    exit 0
fi

# Step 3: Commit changes
echo "Step 3: Committing changes..."
git commit -m "$COMMIT_MESSAGE"

# Step 4: Create version tag
echo "Step 4: Creating version tag v$VERSION..."
git tag "v$VERSION"

# Step 5: Push everything together
echo "Step 5: Pushing commit and tag to GitHub..."
git push origin main --tags

echo "=================================================="
echo "Successfully pushed to GitHub:"
echo "- Commit: $COMMIT_MESSAGE"
echo "- Tag: v$VERSION"
echo "- GitHub Actions should now be building the release"
echo "=================================================="

# Step 6: Show recent runs and monitor the build
echo "Recent GitHub Actions runs:"
gh run list --limit 3

echo ""
echo "Monitoring GitHub Actions build..."

# Get the latest run ID for our tag
RUN_ID=$(gh run list --event push --limit 1 --json databaseId --jq '.[0].databaseId')

if [ -z "$RUN_ID" ]; then
    echo "Could not find GitHub Actions run. Check manually at: https://github.com/$(gh repo view --json owner,name --jq '.owner.login + "/" + .name')/actions"
    exit 0
fi

echo "Monitoring run ID: $RUN_ID"

# Poll every 10 seconds until completion
while true; do
    STATUS=$(gh run view $RUN_ID --json status --jq '.status')
    CONCLUSION=$(gh run view $RUN_ID --json conclusion --jq '.conclusion')
    
    echo "Status: $STATUS$([ "$CONCLUSION" != "null" ] && echo " | Conclusion: $CONCLUSION")"
    
    if [ "$STATUS" = "completed" ]; then
        echo ""
        echo "=================================================="
        if [ "$CONCLUSION" = "success" ]; then
            echo "✅ BUILD SUCCESSFUL!"
            echo "GitHub Actions build completed successfully."
            echo "Release v$VERSION should now be available."
        elif [ "$CONCLUSION" = "failure" ]; then
            echo "❌ BUILD FAILED!"
            echo "GitHub Actions build failed. Error details:"
            echo "--------------------------------------------------"
            gh run view $RUN_ID --log | grep -A 10 -B 10 "Error\|Failed\|##\[error\]" || echo "Could not extract error details"
        else
            echo "⚠️  Build completed with status: $CONCLUSION"
        fi
        echo "=================================================="
        echo "View full log: gh run view --log $RUN_ID"
        echo "View on GitHub: https://github.com/$(gh repo view --json owner,name --jq '.owner.login + "/" + .name')/actions/runs/$RUN_ID"
        break
    fi
    
    sleep 5
done
echo "Script completed successfully!"