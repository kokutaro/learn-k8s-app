#!/bin/bash
set -eux

find . \( -name "*.csproj" -o -name "*.slnx" \) -print0 \
    | tar -cvf projectfiles.tar --null -T -

docker build -t learn-k8s-app-backend:latest -f Dockerfile .

rm projectfiles.tar