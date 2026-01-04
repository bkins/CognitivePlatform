# When to use Source Vs Domain Service Vs Knowledge Service

 - Does this define how a domain behaves? → **Domain Service**

 - Does this change how a domain appears in the inbox or knowledge system? → **Knowledge Source**

 - Does this just decide which domain should handle something? → **Knowledge Service**

## KNOWLEDGE SOURCE (ADAPTER)

---
 Adapts a domain into the Knowledge system.
 - Translates domain objects into KnowledgeItemDto.
 - Implements knowledge lifecycle actions (archive, hide, etc.).
 - May call domain services or persistence, but owns no business rules.

 ### **Rule of thumb:**
 If the Knowledge system disappeared tomorrow,
 this class should be deleted.

## DOMAIN SERVICE

---
 Owns domain meaning and business rules.
 - Defines what this domain object IS and how it behaves.
 - Talks directly to persistence (ObjectStore).
 - Does NOT know about Knowledge, inboxes, UI, or cross-domain concepts.

### **Rule of thumb:**

 If the Knowledge system disappeared tomorrow,
 this service should still exist unchanged.