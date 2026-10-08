import { describe, expect, it } from 'vitest';
import {
  applyCreatedServer,
  type BusinessAccountSummary,
  businessCardKind,
  createBusinessServerErrorMessage,
  pickBusinessCardState,
  shouldReloadAfterError,
} from './businessHosting';

function business(overrides: Partial<BusinessAccountSummary> = {}): BusinessAccountSummary {
  return {
    id: 'b1',
    businessName: 'Acme Studio',
    status: 'pending',
    createdAt: '2026-10-01T00:00:00Z',
    serverId: null,
    serverDid: null,
    ...overrides,
  };
}

describe('businessCardKind', () => {
  it('maps statuses and a linked server', () => {
    expect(businessCardKind(business())).toBe('pending');
    expect(businessCardKind(business({ status: 'approved' }))).toBe('approved');
    expect(businessCardKind(business({ status: 'rejected' }))).toBe('rejected');
    expect(businessCardKind(business({ status: 'approved', serverId: 's1' }))).toBe('has-server');
  });

  it('treats unknown statuses as pending', () => {
    expect(businessCardKind(business({ status: 'something-new' }))).toBe('pending');
  });
});

describe('pickBusinessCardState', () => {
  it('hides the card without business accounts', () => {
    expect(pickBusinessCardState(null)).toEqual({ kind: 'none' });
    expect(pickBusinessCardState([])).toEqual({ kind: 'none' });
  });

  it('prefers an approved business that still needs a server', () => {
    const state = pickBusinessCardState([
      business({ id: 'p', status: 'pending', createdAt: '2026-10-03T00:00:00Z' }),
      business({ id: 'a', status: 'approved' }),
      business({ id: 'h', status: 'approved', serverId: 's1', serverDid: 'did:key:z1' }),
    ]);
    expect(state.kind).toBe('approved');
    expect(state.kind !== 'none' && state.business.id).toBe('a');
  });

  it('then has-server, then pending, then rejected; newest first within a group', () => {
    expect(pickBusinessCardState([
      business({ id: 'r', status: 'rejected' }),
      business({ id: 'h', status: 'approved', serverId: 's1' }),
    ]).kind).toBe('has-server');

    const state = pickBusinessCardState([
      business({ id: 'r', status: 'rejected' }),
      business({ id: 'old', status: 'pending', createdAt: '2026-09-01T00:00:00Z' }),
      business({ id: 'new', status: 'pending', createdAt: '2026-10-02T00:00:00Z' }),
    ]);
    expect(state.kind !== 'none' && state.business.id).toBe('new');

    expect(pickBusinessCardState([business({ status: 'rejected' })]).kind).toBe('rejected');
  });
});

describe('applyCreatedServer', () => {
  it('links the new server to the right business only', () => {
    const updated = applyCreatedServer(
      [business({ id: 'a', status: 'approved' }), business({ id: 'b', status: 'pending' })],
      'a',
      { serverId: 's9', serverDid: 'did:key:z9', name: 'Acme Studio', baseUrl: 'http://localhost:5001', created: true },
    );
    expect(updated[0]).toMatchObject({ serverId: 's9', serverDid: 'did:key:z9', serverBaseUrl: 'http://localhost:5001' });
    expect(updated[1].serverId).toBeNull();
    expect(pickBusinessCardState(updated).kind).toBe('has-server');
  });
});

describe('createBusinessServerErrorMessage', () => {
  it('words each server error code', () => {
    expect(createBusinessServerErrorMessage(403, { code: 'not_approved', status: 'pending' })).toMatch(/waiting for approval/);
    expect(createBusinessServerErrorMessage(403, { code: 'not_approved', status: 'rejected' })).toMatch(/not approved/);
    expect(createBusinessServerErrorMessage(409, { code: 'already_has_server' })).toMatch(/already has/);
    expect(createBusinessServerErrorMessage(502, { code: 'link_failed' })).toMatch(/Try again/);
    expect(createBusinessServerErrorMessage(503, { code: 'auth_unavailable' })).toMatch(/auth server/);
    expect(createBusinessServerErrorMessage(502, { code: 'auth_rejected' })).toMatch(/not set up/);
  });

  it('falls back to the server message or status', () => {
    expect(createBusinessServerErrorMessage(400, { error: 'Name is required.' })).toBe('Name is required.');
    expect(createBusinessServerErrorMessage(500, {})).toBe('Request failed (500).');
  });

  it('reloads state only when it may have changed', () => {
    expect(shouldReloadAfterError({ code: 'already_has_server' })).toBe(true);
    expect(shouldReloadAfterError({ code: 'link_failed' })).toBe(false);
    expect(shouldReloadAfterError({ code: 'auth_unavailable' })).toBe(false);
  });
});
