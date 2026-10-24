#!/bin/bash
set -euo pipefail

awslocal sqs create-queue --queue-name gamex-library-events-dlq
awslocal sqs create-queue --queue-name gamex-library-events \
  --attributes '{"RedrivePolicy":"{\"deadLetterTargetArn\":\"arn:aws:sqs:us-east-1:000000000000:gamex-library-events-dlq\",\"maxReceiveCount\":\"3\"}"}'

awslocal sqs create-queue --queue-name gamex-catalog-events-dlq
awslocal sqs create-queue --queue-name gamex-catalog-events \
  --attributes '{"RedrivePolicy":"{\"deadLetterTargetArn\":\"arn:aws:sqs:us-east-1:000000000000:gamex-catalog-events-dlq\",\"maxReceiveCount\":\"3\"}"}'

awslocal s3 mb s3://gamex-media
