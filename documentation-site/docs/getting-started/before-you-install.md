---
title: Before You Install
description: Understand what you need to self-host Prismedia, how the server and native apps fit together, and which services are optional.
---

# Before You Install

Prismedia runs on a computer or NAS you control. That machine stores the library and serves your media to the web and native apps. It needs to stay running and reachable while you browse or stream from another device.

## What you need

| Piece | Purpose |
| --- | --- |
| A machine that can run Docker | Hosts the Prismedia server. Docker Compose is useful for saving its configuration in a file. |
| A persistent `/data` volume | Stores accounts, catalog information, progress, settings, and generated artwork and playback files. |
| Media folders the server can access | Hold your movies, music, books, and other files. They are mounted separately from `/data`. |
| A browser on the same network | Opens the setup wizard and the complete library management interface. |

The container includes the database, background worker, and media tools. You do not need to install a separate database or build the app from source.

Scanning and generating previews use disk space and processing time. Video that needs conversion during playback is more demanding than direct playback. Begin with a small library and check **Jobs** before increasing background work. See [Playback & Reading](../using/playback.md) for playback behavior.

## Does Prismedia include media?

Prismedia does not come with a film, music, or book catalog to play. You supply the files, or configure your own sources and download clients for acquisition. A metadata search finds information about a title; it does not supply its playable files.

## Can I use my existing folders?

Yes. Mount an existing folder into the container and add its container path as a watched library root. A scan catalogs the files where they are. Folder layout affects how they are grouped, so read [Organize Your Media Folders](./organize-folders.md) before adding a large collection.

For browsing, reading, and playback, you can mount the media folder **read-only**. Uploads, renames, moves, deletes, and acquisition imports need **write access** to their destinations. Generated artwork and playback files are stored under `/data`.

## Do I need indexers or download clients?

Only if you want to acquire media through **Request**. They are optional when scanning files you already have.

For requests, Prismedia uses a metadata plugin to identify the title, your indexer to find releases, and your download client to transfer a selected release. Prismedia then imports the files into the library you chose. See [Requests](../using/requests.md) when you are ready to configure that workflow.

## Do the native apps run the server?

No. They connect to your Prismedia server and use the same account and library as the browser.

The native app is on the [App Store](https://apps.apple.com/us/app/prismedia/id6792944211) for iPhone, iPad, and Apple TV. Start with the web setup, then follow [Connect the Native App](../using/native-apps.md).

## Can other people use my library?

Create a Prismedia account for each household member and choose which libraries they can access. Request permission is also managed per account. See [Authentication & User Accounts](../deployment/authentication.md).

A local server address works only where your home network is reachable. For access away from home, use your private network connection or configure an HTTPS endpoint following the [Reverse Proxy](../deployment/reverse-proxy.md) guide.

## What do I need to back up?

Keep backups of both your media and Prismedia's state. Built-in database backups cover the catalog and account data; they do not copy the media files or replace a full instance backup. See [Backups & Restore](../deployment/backups.md).

## Ready to start

[Install & Run](./install.md) provides Docker Compose and Docker run examples. Then [Organize Your Media Folders](./organize-folders.md) shows how your storage paths become library roots.
