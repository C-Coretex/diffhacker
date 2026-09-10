import { execFileSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync, chmodSync, readdirSync, statSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';

/**
 * Real git repositories in temporary directories.
 *
 * The same decision as the .NET suites: a stubbed git would only prove the stub agrees with
 * itself, and the cases that matter here — a rename detected as a rename, an untracked file that
 * `git diff` alone never shows, a repository with no commits — only exist in real repositories.
 *
 * Every invocation is isolated from the developer's own git configuration. Without that, a
 * machine with `core.autocrlf=true` (the Windows installer default) produces different line
 * counts, and a global `core.excludesFile` can hide a fixture's own files.
 */
export class GitRepo {
  private constructor(
    readonly root: string,
    private readonly isolatedHome: string,
  ) {}

  static create(label: string): GitRepo {
    const root = mkdtempSync(join(tmpdir(), `diffhacker-e2e-${label}-`));
    const repo = new GitRepo(root, `${root}-home`);

    repo.git('init', '--initial-branch=main');
    repo.git('config', 'user.email', 'fixture@diffhacker.test');
    repo.git('config', 'user.name', 'DiffHacker Fixture');
    repo.git('config', 'commit.gpgsign', 'false');
    repo.git('config', 'core.autocrlf', 'false');
    repo.git('config', 'core.safecrlf', 'false');

    return repo;
  }

  /** A bare repository: no working tree, so nothing for this application to review. */
  static createBare(label: string): GitRepo {
    const root = mkdtempSync(join(tmpdir(), `diffhacker-e2e-${label}-`));
    const repo = new GitRepo(root, `${root}-home`);
    repo.git('init', '--bare', '--initial-branch=main');
    return repo;
  }

  /** A plain directory that is not a repository at all. */
  static createPlainDirectory(label: string): GitRepo {
    const root = mkdtempSync(join(tmpdir(), `diffhacker-e2e-${label}-`));
    return new GitRepo(root, `${root}-home`);
  }

  path(relative: string): string {
    return join(this.root, relative.split('/').join(sep()));
  }

  write(relative: string, contents: string): this {
    const target = this.path(relative);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, contents, 'utf8');
    return this;
  }

  writeBytes(relative: string, contents: Uint8Array): this {
    const target = this.path(relative);
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, contents);
    return this;
  }

  /** A file git will call binary: a NUL byte inside the first 8000. */
  writeBinary(relative: string, length = 512): this {
    const bytes = new Uint8Array(length);
    for (let index = 0; index < length; index++) {
      bytes[index] = index % 251;
    }
    bytes[1] = 0;
    return this.writeBytes(relative, bytes);
  }

  /** `count` numbered text files under one directory, for the paging case. */
  writeMany(directory: string, count: number, body: (index: number) => string): this {
    for (let index = 0; index < count; index++) {
      this.write(`${directory}/file${index}.cs`, body(index));
    }
    return this;
  }

  remove(relative: string): this {
    rmSync(this.path(relative));
    return this;
  }

  stage(...relatives: string[]): this {
    this.git('add', '--', ...relatives);
    return this;
  }

  stageAll(): this {
    this.git('add', '--all');
    return this;
  }

  rename(from: string, to: string): this {
    this.git('mv', from, to);
    return this;
  }

  commit(message: string): this {
    this.git('commit', '--no-gpg-sign', '-m', message);
    return this;
  }

  /** Stages everything and commits, the usual "make a baseline" move. */
  commitAll(message: string): this {
    return this.stageAll().commit(message);
  }

  git(...args: string[]): string {
    return execFileSync('git', args, {
      cwd: this.root,
      encoding: 'utf8',
      env: {
        ...process.env,
        GIT_TERMINAL_PROMPT: '0',
        GIT_CONFIG_NOSYSTEM: '1',
        GIT_CONFIG_GLOBAL: join(this.isolatedHome, '.gitconfig'),
        HOME: this.isolatedHome,
        USERPROFILE: this.isolatedHome,
        XDG_CONFIG_HOME: join(this.isolatedHome, '.config'),
      },
    });
  }

  dispose(): void {
    deleteTree(this.root);
    deleteTree(this.isolatedHome);
  }
}

/**
 * Tracks the repositories a test built so they are all removed afterwards, whether the test
 * passed or not.
 */
export class RepoSet {
  private readonly repos: GitRepo[] = [];

  /**
   * A working tree carrying every awkward case at once: a file edited both staged and unstaged,
   * a staged addition, a deletion, a rename, a binary, an untracked file, a gitignored file, and
   * a nested manifest so project attribution has something to resolve.
   */
  awkward(): GitRepo {
    const repo = this.track(GitRepo.create('awkward'));

    repo
      .write('.gitignore', 'ignored.env\n')
      .write('package.json', '{ "name": "root" }\n')
      .write('src/Web/package.json', '{ "name": "web" }\n')
      .write('src/Web/edited.ts', 'export const a = 1;\nexport const b = 2;\nexport const c = 3;\n')
      .write('docs/original.md', longText(40))
      .write('removed.txt', 'goodbye\n')
      .writeBinary('assets/logo.png')
      .commitAll('baseline');

    // Staged and unstaged edits to the same file: it must appear once, with both counted.
    repo
      .write('src/Web/edited.ts', 'export const a = 1;\nexport const staged = 2;\nexport const c = 3;\n')
      .stage('src/Web/edited.ts')
      .write(
        'src/Web/edited.ts',
        'export const a = 1;\nexport const staged = 2;\nexport const c = 3;\nexport const unstaged = 4;\n',
      );

    repo
      .write('src/Web/addedStaged.ts', 'export const added = true;\n')
      .stage('src/Web/addedStaged.ts');

    repo.remove('removed.txt');
    repo.rename('docs/original.md', 'docs/renamed.md');
    repo.writeBinary('assets/logo.png', 1024);
    repo.write('src/Web/brandNew.tsx', 'export const New = () => null;\n');
    repo.write('ignored.env', 'TOKEN=must-not-appear\n');

    return repo;
  }

  /** A committed repository with nothing outstanding. */
  clean(): GitRepo {
    return this.track(GitRepo.create('clean').write('readme.md', 'clean\n').commitAll('initial'));
  }

  /**
   * A change that genuinely spans three concerns: a migration, the token that reads it, and the
   * endpoint that returns it, plus a changelog line.
   *
   * Iteration 11's verification steps 2 and 3 cannot be checked on a fixture whose files are all
   * alike. The whole question is whether dependency flow keeps a cross-concern path in one cluster
   * while change clusters splits it by area, and that needs areas — so the directories here are the
   * point of the fixture rather than decoration.
   */
  layered(): GitRepo {
    const repo = this.track(GitRepo.create('layered'));

    repo
      .write('db/001_add_tenant.sql', 'ALTER TABLE users ADD COLUMN tenant TEXT;\n')
      .write('db/schema.sql', 'CREATE TABLE users (id INTEGER PRIMARY KEY);\n')
      .write('auth/tokens.ts', 'export interface Token {\n  subject: string;\n}\n')
      .write('auth/middleware.ts', 'export function authenticate() {\n  return true;\n}\n')
      .write('api/users.ts', 'export function listUsers() {\n  return [];\n}\n')
      .write('api/routes.ts', 'export const routes = ["/users"];\n')
      .write('docs/changelog.md', '# Changelog\n')
      .commitAll('initial');

    repo
      .write('db/001_add_tenant.sql', 'ALTER TABLE users ADD COLUMN tenant TEXT NOT NULL;\n')
      .write('db/schema.sql', 'CREATE TABLE users (id INTEGER PRIMARY KEY, tenant TEXT);\n')
      .write('auth/tokens.ts', 'export interface Token {\n  subject: string;\n  tenant: string;\n}\n')
      .write('auth/middleware.ts', 'export function authenticate() {\n  return tenantOf() !== null;\n}\n')
      .write('api/users.ts', 'export function listUsers(tenant: string) {\n  return [tenant];\n}\n')
      .write('api/routes.ts', 'export const routes = ["/users", "/tenants"];\n')
      .write('docs/changelog.md', '# Changelog\n\n- Users are now scoped to a tenant.\n');

    return repo;
  }

  /** No commits at all, so there is no HEAD to compare against. */
  withoutCommits(): GitRepo {
    const repo = this.track(GitRepo.create('nocommits'));
    repo.write('first.cs', 'class First;\n').stage('first.cs');
    repo.write('second.cs', 'class Second;\n');
    return repo;
  }

  /** More changed files than the list reveals at once. */
  large(files: number): GitRepo {
    const repo = this.track(GitRepo.create('large'));
    repo.write('go.mod', 'module fixture\n');
    repo.writeMany('internal', files, (index) => `// baseline ${index}\n`);
    repo.commitAll('baseline');
    repo.writeMany('internal', files, (index) => `// baseline ${index}\n// edited\n`);
    return repo;
  }

  /**
   * One of every file kind the diff viewer has to open sensibly — Iteration 10 requirement 8, and
   * its verification step 3 — plus a file past the size at which the viewer stops highlighting.
   *
   * `awkward()` covers the same ground for the changed-file list, but this one is built around what
   * the *viewer* has to survive rather than what the list has to count, and it is deliberately small
   * enough that every node fits on the diagram at once.
   */
  reviewable(): GitRepo {
    const repo = this.track(GitRepo.create('reviewable'));

    repo
      .write('src/cache.ts', longText(30))
      .write('src/old-name.ts', 'export const moved = true;\n')
      .write('src/gone.ts', 'export const doomed = true;\n')
      .writeBinary('assets/logo.png')
      .commitAll('baseline');

    // modified — and long enough that a node naming lines 20 to 24 has somewhere to scroll to.
    repo.write('src/cache.ts', `${longText(30)}export const tenantAware = true;\n`);

    // renamed, deleted, added, untracked-in-the-same-breath, and a binary that changed.
    repo.rename('src/old-name.ts', 'src/new-name.ts');
    repo.remove('src/gone.ts');
    repo.write('src/brand-new.ts', 'export const fresh = 1;\n');
    repo.writeBinary('assets/logo.png', 2048);

    // ~1.7 MB: past the megabyte at which the viewer drops highlighting, and well under the five
    // the host refuses outright, so it exercises the band between the two. Each line is about eleven
    // bytes, which is where the count comes from.
    repo.write('src/huge.ts', longText(150_000));

    return repo;
  }

  bare(): GitRepo {
    return this.track(GitRepo.createBare('bare'));
  }

  plainDirectory(): GitRepo {
    return this.track(GitRepo.createPlainDirectory('plain'));
  }

  track(repo: GitRepo): GitRepo {
    this.repos.push(repo);
    return repo;
  }

  disposeAll(): void {
    for (const repo of this.repos.splice(0)) {
      repo.dispose();
    }
  }
}

function longText(lines: number): string {
  return `${Array.from({ length: lines }, (_, index) => `line ${index}`).join('\n')}\n`;
}

function sep(): string {
  return process.platform === 'win32' ? '\\' : '/';
}

function deleteTree(directory: string): void {
  try {
    // git marks objects read-only, which blocks a plain recursive delete on Windows.
    clearReadOnly(directory);
    rmSync(directory, { recursive: true, force: true, maxRetries: 3 });
  } catch {
    // A leftover temp directory is not worth failing a test over.
  }
}

function clearReadOnly(directory: string): void {
  let entries: string[];
  try {
    entries = readdirSync(directory);
  } catch {
    return;
  }

  for (const entry of entries) {
    const target = join(directory, entry);
    try {
      if (statSync(target).isDirectory()) {
        clearReadOnly(target);
      } else {
        chmodSync(target, 0o666);
      }
    } catch {
      // Nothing useful to do about one stubborn file.
    }
  }
}
