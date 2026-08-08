# Governance Model

The `EricksonLopez.Outbox` project operates under a benevolent dictator for now (BDFL) model, transitioning towards a meritocratic consensus model as the community grows. This document outlines how decisions are made, how roles are assigned, and how the project evolves.

## 1. Roles and Responsibilities

### Core Maintainers
Core maintainers have write access to the repository, can merge Pull Requests, and cut releases.
- **Erickson López** (Creator & BDFL)

*Responsibilities:*
- Reviewing and merging architectural changes.
- Ensuring the "Zero-Allocation" and "NativeAOT" guarantees are never compromised.
- Managing NuGet releases and security patches.

### Contributors
Anyone who submits a Pull Request, reports an issue, or helps answer questions in Discussions is a contributor.
- Significant and sustained contributions (e.g., implementing a new Broker provider, maintaining a Storage engine) may lead to an invitation to become a Core Maintainer.

## 2. Decision Making Process

For minor bug fixes and non-breaking features, standard Pull Request reviews are sufficient.

### Architectural Changes (ADRs)
For massive changes (e.g., changing how the Dispatcher handles concurrency, adding a new required dependency, altering the public API), the project requires an **Architecture Decision Record (ADR)**.

1. **Propose**: A contributor opens a PR adding a new markdown file to `docs/adr/` outlining the Context, Decision, and Trade-offs.
2. **Debate**: The community and maintainers debate the ADR in the PR comments.
3. **Consensus**: If consensus is reached (or a final decision is made by the BDFL), the ADR is merged, marking its status as `Accepted`.
4. **Implementation**: Only after the ADR is merged can the actual code changes be submitted.

## 3. Principles of the Project

When making decisions, maintainers must adhere to the inflexible principles of the project (as in the ADRs):

1. **Zero Reflection / Zero Magic**: No `Activator.CreateInstance`, no dynamic proxies. Everything must be statically analyzable.
2. **NativeAOT First**: Any feature that breaks Trimming or AOT compilation will be rejected.
3. **Zero Allocation in Hot Paths**: The core dispatcher must not allocate on the Gen 0 heap.
4. **Database Agnostic, but Optimized**: The core must not know about SQL, but the storage implementations must exploit engine-specific features (like `SKIP LOCKED`).
