export type LeadStatus = "Open" | "Won" | "Lost" | "Archived";
export type StageKind = "Open" | "Won" | "Lost";
export type PurchaseTimeframe = "Unknown" | "Immediate" | "UpTo3Months" | "UpTo6Months" | "UpTo12Months" | "Over12Months";

export type Me = {
  userId: string;
  email: string;
  displayName: string;
  organizationId: string;
  organizationName: string;
  teamId: string | null;
  role: string;
  /** Somente para UX. A autorização real é feita pela API. */
  capabilities: Record<string, "own" | "team" | "org">;
};

export type LeadCard = {
  id: string;
  contactName: string;
  phone: string | null;
  ownerUserId: string;
  ownerName: string;
  stageId: string;
  status: LeadStatus;
  source: string | null;
  interest: string | null;
  score: number | null;
  tags: string[];
  enteredAt: string;
  stageEnteredAt: string;
  lastContactAt: string | null;
  nextFollowUpAt: string | null;
  version: number;
};

export type BoardStage = {
  id: string;
  name: string;
  kind: StageKind;
  color: string | null;
  position: number;
  total: number;
  leads: LeadCard[];
};

export type Board = { pipelineId: string; pipelineName: string; stages: BoardStage[] };

export type LeadDetail = {
  id: string;
  contactId: string;
  fullName: string;
  phone: string | null;
  email: string | null;
  ownerUserId: string;
  ownerName: string;
  pipelineId: string;
  stageId: string;
  stageName: string;
  status: LeadStatus;
  source: string | null;
  campaign: string | null;
  interest: string | null;
  developmentId: string | null;
  lotId: string | null;
  budgetMinCents: number | null;
  budgetMaxCents: number | null;
  timeframe: PurchaseTimeframe;
  region: string | null;
  notes: string | null;
  tags: string[];
  score: number | null;
  lostReason: string | null;
  enteredAt: string;
  stageEnteredAt: string;
  lastContactAt: string | null;
  nextFollowUpAt: string | null;
  version: number;
};

export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number };

export type HistoryItem = {
  occurredAt: string;
  action: string;
  actorKind: "User" | "Copilot" | "System";
  actorId: string | null;
  actorName: string | null;
  data: string;
};

export type UserSummary = { userId: string; displayName: string; teamId: string | null; role: string };

export type Stage = { id: string; name: string; kind: StageKind; color: string | null; position: number; isArchived: boolean };
export type Pipeline = { id: string; name: string; isDefault: boolean; stages: Stage[] };
