# AGI PDM Migrator Documentation

This folder contains documentation for the AGI PDM Server Migration Tool.

## Files

- **manual_procedure.pdf** - Original manual migration procedure that this tool automates
- **Configuration.md** - Configuration file format and options
- **Usage.md** - How to use the migration tool

## Overview

The AGI PDM Migrator automates the process of migrating SolidWorks PDM vault views from one server to another. This tool replaces the manual process described in manual_procedure.pdf with an automated solution that can be deployed via RMM or run locally.

## Key Features

- Automated migration of PDM vault views
- Pre-flight checks to ensure system readiness
- Registry backup before modifications
- Automatic cleanup of local cache
- Autonomous mode for RMM deployment
- Detailed logging for troubleshooting

## Quick Start

1. Download the latest release
2. Configure `config.json` with your server details
3. Run as Administrator
4. Monitor the logs for progress

For detailed instructions, see Usage.md.