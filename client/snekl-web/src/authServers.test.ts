import { describe, expect, it } from 'vitest';
import { authServerLabel, pickAuthServer, uniqueAuthServers } from './authServers';

const issuers = [
  { name: 'Snekl Auth Alpha', issuerUrl: 'http://localhost:5100/' },
  { name: 'Snekl Auth Beta', issuerUrl: 'http://localhost:5101' },
  { name: 'Snekl Auth Alpha', issuerUrl: 'http://localhost:5100' },
];

describe('uniqueAuthServers', () => {
  it('drops blank and duplicate issuer URLs', () => {
    expect(uniqueAuthServers([...issuers, { name: 'Empty', issuerUrl: '  ' }])).toEqual([
      { name: 'Snekl Auth Alpha', issuerUrl: 'http://localhost:5100' },
      { name: 'Snekl Auth Beta', issuerUrl: 'http://localhost:5101' },
    ]);
  });
});

describe('pickAuthServer', () => {
  const options = uniqueAuthServers(issuers);

  it('keeps a remembered URL that is still published', () => {
    expect(pickAuthServer(options, 'http://localhost:5101/')).toBe('http://localhost:5101');
  });

  it('falls back to the first published auth when the remembered URL is not in the list', () => {
    expect(pickAuthServer(options, 'http://elsewhere.example')).toBe('http://localhost:5100');
    expect(pickAuthServer([], 'http://localhost:5100')).toBe('');
  });
});

describe('authServerLabel', () => {
  it('shows the name and host', () => {
    expect(authServerLabel({ name: 'Snekl Auth Alpha', issuerUrl: 'http://localhost:5100' })).toBe(
      'Snekl Auth Alpha (localhost:5100)',
    );
  });
});
