# Delete all questions from forum
aws dynamodb scan --table-name rerythm-prod-forum-questions --attributes-to-get QuestionId --output json | `
ConvertFrom-Json | Select-Object -ExpandProperty Items | ForEach-Object {
    $questionId = $_.QuestionId.S
    Write-Host "Deleting question: $questionId"
    aws dynamodb delete-item --table-name rerythm-prod-forum-questions --key "{\"QuestionId\":{\"S\":\"$questionId\"}}"
}

# Delete all answers from forum
aws dynamodb scan --table-name rerythm-prod-forum-answers --attributes-to-get AnswerId --output json | `
ConvertFrom-Json | Select-Object -ExpandProperty Items | ForEach-Object {
    $answerId = $_.AnswerId.S
    Write-Host "Deleting answer: $answerId"
    aws dynamodb delete-item --table-name rerythm-prod-forum-answers --key "{\"AnswerId\":{\"S\":\"$answerId\"}}"
}

Write-Host "Forum cleanup complete"
