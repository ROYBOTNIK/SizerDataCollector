# Sizer event service (reference)

Compiled from Compac Sizer API help (CHM). Use this when designing event-driven refresh for product setup, batches, and packs. Operator-facing behaviour for polled metrics is in [PRODUCT_SETUP_WORKFLOW.md](PRODUCT_SETUP_WORKFLOW.md).

## Quick reference

### Callback contract (`IEventCallback`)

Namespace: `Compac.Services.Core` (assembly `Compac.Services.Core.dll`).

| Member | Description |
| --- | --- |
| `void OnEvent(ServiceEvent serviceEvent)` | Invoked when a subscribed event occurs. |

Each `ServiceEvent` carries `EventName`, `EventTime`, `Id` (Int64 in Sizer 14.0+), and `EventArgs` (`Dictionary<string, string>`). The collector already has a generated `ServiceEvent` type in the Sizer WCF reference; event-service integration must use the same shape.

### Supported event names (`SizerEventServiceEventNames`)

Static class in `Compac.Services.Sizer.Service`. Property `All` returns every defined name.

| Constant | Typical use for CollectorAgent |
| --- | --- |
| `BatchChange` | Batch change completed; refresh batch context and likely `product_setup` (variety/layout may change). |
| `BatchChangeStarted` | Batch change began; optional early signal before fruit stabilises. |
| `BatchStageChange` | Staging change in progress. |
| `BatchStageChanged` | Staging state settled; may align with lane-group batch routing. |
| `PackComplete` | A pack finished; pack/order/batch fields in `EventArgs` (see 14.0 notes below). Good for pack traceability, not full layout definitions. |
| `OrderCompleted` | Order fulfillment (14.0+). |
| `OrderFinished` | Order fulfillment (14.0+). |
| `OrderLineCompleted` | Order line fulfillment (14.0+). |
| `OrderLineFinished` | Order line fulfillment (14.0+). |
| `ReSync` | Re-synchronise signal; treat as “refresh cached Sizer state” (exact semantics: see Compac event page). |
| `UserDataChanged` | User data changed; may relate to product custom fields — confirm `EventArgs` keys in help. |
| `UserDataFieldChanged` | Single user-data field changed. |

**Not in this list:** there is no dedicated `LayoutChanged`, `ProductUpdated`, or `OutletAssignmentChanged` event name. Outlet moves and in-place product definition edits are **not** guaranteed to emit a layout-specific event; rely on `GetOutlets()` for live positions and use batch / `ReSync` / `UserData*` events plus `GetActiveLayout` refresh when those fire.

### Suggested initial subscription (product setup + batches)

Use explicit names where possible (Compac recommends avoiding `*` in production):

```text
BatchChange,BatchChangeStarted,BatchStageChanged,PackComplete,ReSync,UserDataChanged
```

Or wildcard: `Batch*,PackComplete,ReSync,UserData*` (uses **global** event ids if you also replay history — see wildcards section below).

On `OnEvent`, map `serviceEvent.EventName` to actions such as: refresh `product_setup` snapshot, persist pack event row, or invalidate variety/layout cache.

### Integration paths (pick one)

| API | Transport | Callback |
| --- | --- | --- |
| `ISizerEventService` | SOAP only | `IEventCallback.OnEvent` (duplex WCF) |
| `ISizerCallbackEventService` | WebAPI only | HTTP callback (see Compac help for URL contract) |
| `ISizerLongPollingEventService` | HTTP | Poll `GetNewEvents`; fallback when callbacks blocked |

---

## Raw API excerpts (CHM)

This service provides access to events which occur in the sizer software. In order to use this service, the client has to implement the IEventCallback interface. 
Important note Important 
This service is only available as SOAP; it cannot be called using a WebAPI binding. For that, see ISizerCallbackEventService. 


Namespace:  Compac.Services.Sizer.Service
Assembly:  Compac.Services.Sizer.Service (in Compac.Services.Sizer.Service.dll) Version: 1.0.0.0 (1.0.0.0)
Syntax
C#VBC++WebAPI
Copypublic interface ISizerEventService : ICompacService
Public Interface ISizerEventService
	Inherits ICompacService
public interface class ISizerEventService : ICompacService
Not applicable.
The ISizerEventService type exposes the following members.

Methods
   Name Description 
Public method GetEvent Get an event  
Public method GetEvents Get a list of events  
Public method GetLastEventId Get the id of the last event recorded  
Public method GetVersion Gets the ServiceVersion of the service. 
(Inherited from ICompacService.) 
Public method Subscribe Subscribe to an event with the given name.  
Public method UnSubscribe Unsubscribe from an event with the given name.  

The Compac.Services.Sizer.Service namespace defines the service contract for the Sizer and SizerEvent services, which are implemented by the Sizer software. (They may also be implemented in part by other software.)
Classes
   Class Description 
Public class AbstractSamplingConfiguration The interface for describing different types of Sampling Configurations  
Public class Batch A Batch. This represents a block of time running fruit from a single grower (either from a single bin or multiple bins) and a single variety and (initially) a single layout.  
Public class BatchStaging A summary of batch staging information.  
Public class DownstreamElementSet Element set for a downstream outlet. 
Public class DownstreamOutletConfiguration Configuration information for a Downstream Outlet. 
Public class DownstreamRateLimit Rate limit for a Downstream Outlet. 
Public class Element An Element is a Size/Grade/Quality combination. Every fruit gets assigned to a particular Element. It represents the Quality the fruit is, the Grade, and what Size it fits into. Additionally it also represents what Label should be on the fruit  
Public class ElementSamplingConfiguration Sampling by elements. In this configuration you can choose arbitrary combinations of Quantities/Sizes/Grades, and set a limit  
Public class Grade A Grade  
Public class Grower A Grower and it's details. Except for Code and Name all fields are optional.  
Public class Label A Label is applied to fruit by a Labeler. 
Public class LaneGroup A Group of Lanes to put a Batch on  
Public class Layout A layout. A layout is defined by assignments of products to outlets.  
Public class Outlet An outlet on the Sizer machine. It contains two critical pieces of information, the name of the outlet and the Id. The Id is used by many operations to reference the outlet.  
Public class Pack A pack type.  
Public class PackBoxType A box description.  
Public class PackControl Contains information about pack delivery and control. 
Public class PackingStyle A box description.  
Public class PackTargetFill Describes the target quantity/weight of a Product or Pack. 
Public class Product A product. Products package together a set of size/grade/qualities, the pack type they should go into, How much fruit should be put into the pack, and the label that should be put on the fruit. In other words, they represent the end result that should be delivered from an outlet.  
Public class ProductSamplingConfiguration Sampling by elements. In this configuration you can choose arbitrary combinations of Quantities/Sizes/Grades, and set a limit  
Public class Quality A Quality  
Public class QualityGradePair A Quality and a Grade.  
Public class Ratio A sampling ratio. 
Public class SamplingProgress Holds information about the state or progress of a SampleConfiguration at an outlet  
Public class Size A Size defines the parameters a fruit must have in order to be this Size and it has also additional information assigned.  
Public class SizerEventServiceEventNames The names of the vents currently supported by the ISizerEventService.  
Public class SizingMap A SizingMap is a list of Sizes and their corresponding settings. The Sizing Map is associated with a set of Quality/Grade combinations. Most common is 1 Sizing Map for all the Qualities and Grades. However sometimes a particular Quality will be split out and sized separately or/and a Grade is separated out and sized differently.  
Public class Stage A batch staging point  
Public class Variety A Variety. Defines parameters of how the fruits are being sorted (Sizes, Grades, Qualities). Also acts as a container for Layouts and Products.  

Interfaces
   Interface Description 
Public interface ISizerCallbackEventService This service is similar to the ISizerEventService, but is designed for plain HTTP callbacks. 
Important note Important 
This service is only available as WebAPI; it cannot be called using a SOAP binding. For that, see ISizerEventService. 
 
Public interface ISizerEventService This service provides access to events which occur in the sizer software. In order to use this service, the client has to implement the IEventCallback interface. 
Important note Important 
This service is only available as SOAP; it cannot be called using a WebAPI binding. For that, see ISizerCallbackEventService. 
 
Public interface ISizerLongPollingEventService This service is similar to the ISizerEventService, but is designed for long-polling queries instead of a callback interface, and can therefore be used over plain HTTP connections.

Using the callback service instead is preferred, where possible.
 
Public interface ISizerService Service for controlling the Sizer Software.  

Enumerations
   Enumeration Description 
Public enumeration AlarmPriority The Alarm Priority.  
Public enumeration PackFillMode The fill target method.  
Public enumeration RatioMeasurementUnit The Units used for the right hand unit of the ratio when sampling  
Public enumeration SamplingMeasurementUnit The Units used to count fruit when sampling  

The ISizerEventService type exposes the following members.

Methods
   Name Description 
Public method GetEvent Get an event  
Public method GetEvents Get a list of events  
Public method GetLastEventId Get the id of the last event recorded  
Public method GetVersion Gets the ServiceVersion of the service. 
(Inherited from ICompacService.) 
Public method Subscribe Subscribe to an event with the given name.  
Public method UnSubscribe Unsubscribe from an event with the given name.  


The following API changes occurred between Sizer 12.2 and Sizer 14.0:

Downstream Sorter Outlets
Order Fulfillment
Batches
64-bit event ids
Miscellaneous
Downstream Sorter Outlets
Added
 Product.DownstreamConfig
 

Standard product and outlet APIs are used to interact with downstream outlets. In particular, you can use GetActiveLayout to get the currently active layout (including the configuration of downstream outlets), and either AddProduct + ChangeProductAtOutlet (or UpdateLayout) to create a new downstream outlet configuration from scratch, or UpdateProduct to modify the existing downstream outlet configuration on an outlet. You can similarly update offline layouts.

DownstreamConfig must be null for all "standard" products that are assigned to standard outlets.

It must be non-null for a product assigned to a downstream outlet.

It contains the configuration (element sets and rate limits) of the downstream outlet, as in the UI editor.

Note that the Elements and TargetFill of the containing Product are ignored for updates, although Elements will contain the union of enabled element sets when queried, for convenience.

An empty downstream outlet provides no particular indication that it is one, except that it will not accept products without a DownstreamConfig. It is assumed that integrators will know which outlets are intended to be downstream outlets.

You will receive an ArgumentFault (and no action will be performed) if you specify something inappropriate, such as trying to assign a regular product to a downstream outlet or vice versa.

Because downstream outlet configurations are stored per-layout, and because the active layout is unique from all saved layouts, if you modify a saved layout and then it is loaded onto the machine, the ids of the downstream configuration Products will change. Use care when interacting with both saved and active layouts.

Note Note 
The API currently does not completely enforce this uniqueness and will allow you to assign the same product to multiple layouts, which will then temporarily be updated across all layouts if you or the operator make further updates to it. However certain actions will "split" the product again, so you should not rely on this. 

Products with non-null DownstreamConfigs that are not assigned to any outlets will automatically be deleted when the software restarts. (But you should still be tidy and use DeleteProduct if you're intentionally discarding a product.)

Order Fulfillment
Added
 Product.PacksPerPallet

PackComplete event: IsComplete, OrderId, OrderName, OrderUserId

OrderCompleted, OrderFinished, OrderLineCompleted, OrderLineFinished events
 

Batches
Addition of the "batch finishing" feature, batch guids, and batch staging details. Also extended support for totaling codes. 

Added
 ChangeBatch

ChangeBatchByLane

SetBatchDetails

FinishAllBatches

FinishBatch

ReactivateAllBatches

ReactivateBatch

GetStagedBatches

Batch.Guid

Batch.CyclePackIds

BatchChange event: BatchGuid

BatchChangeStarted event: BatchGuid

BatchStageChange event: BatchGuid

PackComplete event: BatchGuid, BatchGuids

BatchStageChanged event
 
Changed
 ClearBatches
 

64-bit event ids
Event ids in the event services are now 64-bit rather than 32-bit, to allow for some sites where particular events are raised very frequently. Despite the type change being considered a breaking change, the APIs remain mostly compatible -- older clients should still be able to connect to new Sizer (until the event id does cross above 32-bit, at which point they will stop working until upgraded), and newer clients should still be able to connect to older Sizer (provided that the event id doesn't max out). 

It is strongly recommended to update clients to use 64-bit values, especially if persisting the last-known-event-id across restarts. 

Changed
 ServiceEvent.Id

LatestServiceEventsList.LatestEventIdProcessed

ISizerEventService.GetLastEventId

ISizerEventService.GetEvent

ISizerEventService.GetEvent

ISizerLongPollingEventService.GetLastEventId

ISizerLongPollingEventService.GetNewEvents
 

Miscellaneous
Added
 Web API is now supported.

UserId

SetOutletOutOfOrder

GetTotalFPMForAllLanes
 
Changed
 GetActiveLayout

SetOutletEnabled

Product.Element.Label

GetLaneGradeFPM, GetLanesGradeFPM, GetLaneSizeFPM, GetLanesSizeFPM

ISizerEventService. Subscribe now supports Event Wildcards.

Size. Settings now includes the totaling category and bridging fruit configuration
 


Get the id of the last event recorded 

Namespace:  Compac.Services.Sizer.Service
Assembly:  Compac.Services.Sizer.Service (in Compac.Services.Sizer.Service.dll) Version: 1.0.0.0 (1.0.0.0)
Syntax
C#VBC++WebAPI
Copylong GetLastEventId(
	string eventName
)
Function GetLastEventId ( 
	eventName As String
) As Long
long long GetLastEventId(
	String^ eventName
)
Not applicable.
Parameters
eventName
Type: System.String
The name of the event. This must be exact; wildcards are not supported.
Return Value
Type: Int64
The id of the last event of the given type


Get an event 

Namespace:  Compac.Services.Sizer.Service
Assembly:  Compac.Services.Sizer.Service (in Compac.Services.Sizer.Service.dll) Version: 1.0.0.0 (1.0.0.0)
Syntax
C#VBC++WebAPI
CopyServiceEvent GetEvent(
	long eventId,
	string eventName
)
Function GetEvent ( 
	eventId As Long,
	eventName As String
) As ServiceEvent
ServiceEvent^ GetEvent(
	long long eventId, 
	String^ eventName
)
Not applicable.
Parameters
eventId
Type: System.Int64
The id of the event
eventName
Type: System.String
The name of the event. This must be exact; wildcards are not supported.
Return Value
Type: ServiceEvent
The event or null if event with the given id doesn't exist

Get a list of events 

Namespace:  Compac.Services.Sizer.Service
Assembly:  Compac.Services.Sizer.Service (in Compac.Services.Sizer.Service.dll) Version: 1.0.0.0 (1.0.0.0)
Syntax
C#VBC++WebAPI
CopyList<ServiceEvent> GetEvents(
	long fromEventId,
	long toEventId,
	string eventName
)
Function GetEvents ( 
	fromEventId As Long,
	toEventId As Long,
	eventName As String
) As List(Of ServiceEvent)
List<ServiceEvent^>^ GetEvents(
	long long fromEventId, 
	long long toEventId, 
	String^ eventName
)
Not applicable.
Parameters
fromEventId
Type: System.Int64
The id of the first event
toEventId
Type: System.Int64
The id of the last event
eventName
Type: System.String
The name of the event. This must be exact; wildcards are not supported.
Return Value
Type: List<ServiceEvent>
A list of all events with ids between fromEventId and toEventId (including the bounds)

Subscribe to an event with the given name. 

Namespace:  Compac.Services.Sizer.Service
Assembly:  Compac.Services.Sizer.Service (in Compac.Services.Sizer.Service.dll) Version: 1.0.0.0 (1.0.0.0)
Syntax
C#VBC++WebAPI
Copybool Subscribe(
	string eventName
)
Function Subscribe ( 
	eventName As String
) As Boolean
bool Subscribe(
	String^ eventName
)
Not applicable.
Parameters
eventName
Type: System.String
The name of the event to subscribe to. Wildcards are supported.
Return Value
Type: Boolean
true if subscription was successful, false otherwise
Remarks
See SizerEventServiceEventNames for the list of supported events (and links to their documentation).

See the individual pages for event revision history and parameter information.


This topic contains the following sections:

Star Wildcard
Multi-Select Wildcards
Case Insensitivity
Event ids and wildcards
Originally, ISizerEventService. Subscribe only supported an exact match on the event name, from among those listed in SizerEventServiceEventNames. Other names would be accepted (and will return true) but you would not receive any events for them. 

With the introduction of the long-polling services in Sizer 11.0, ISizerLongPollingEventService.GetNewEvents(String, Int32) also accepted basic * wildcards, allowing you to subscribe to all batch-related events with Batch* for example, or even all events with simply *. (Note: for performance reasons on both sides it is strongly recommended to only subscribe to events that you actually wish to process.) 

Starting with Sizer 14.0, additional multi-select wildcards are now available, and wildcards are now supported by ISizerEventService.Subscribe, UnSubscribe, and of course ISizerCallbackEventService. Subscribe and UnSubscribe as well. In addition, if you do not specify any valid event names when you subscribe (after wildcard expansion), then it will return false. 

Note however that the other service methods (such as GetLastEventId) still require an exact match, as it doesn't make much sense to use multiple events for those. 

Star Wildcard
The * wildcard will match any part of an event name (zero or more characters). 

By itself, * matches all event names. 

As a prefix, *Changed matches all event names ending with Changed. 

As a suffix, Batch* matches all event names starting with Batch. 

As a surround, *Change* matches all event names containing Change. 

As an embed, Order*Completed matches all events starting with Order and ending with Completed (including OrderCompleted itself). 

Other combinations are possible, of course. 

Multi-Select Wildcards
Starting in Sizer 14.0, you can now specify multiple event names (or patterns including star wildcards) separated by one of comma, semicolon, or pipe (,;|). 

For example, PackComplete,Batch* subscribes to the PackComplete event and all events matching Batch*. 

Case Insensitivity
Starting in Sizer 14.0, event names will match even if there is a difference in case from the "real" event name. (At least according to the regular English rules about upper and lower case characters.) 

It is still recommended to use the proper casing, however, especially as the APIs that do not support wildcards continue to be case-sensitive. 

Event ids and wildcards
Ordinarily, each event type has a separate and overlapping id space -- thus it is possible to receive event id 102 for event A after you've already received event id 842 for event B. Similarly, you can receive the same event id for two different events at different times, when those are of different types. Each sequence of ids is independent. 

However, these independent event ids can only be used when you are subscribing to specific events individually, since when querying the event history you need to specify the latest event id that you are aware of. When you are subscribing using wildcards, you will instead receive (and need to provide) a global id sequenced across all event types (such that event id 104 is always after event id 102, regardless of event type). 

The global ids are completely independent from the per-event-type ids. Care should be taken if switching between using wildcards or not, as you cannot make any assumptions about compatibility of ids between these two modes -- depending on how you were subscribed/querying at the time, you may receive the same event with two completely different ids. 

If you're only listening to "live" events via the callback services, then you can largely ignore the event ids and just process the events as they arrive -- they will always be new events. This only becomes relevant when using the long-polling service or when using GetEvents or similar to retrieve "missed" events that occurred while your client was not running. 







Namespace:  Compac.Services.Sizer.Service
Assembly:  Compac.Services.Sizer.Service (in Compac.Services.Sizer.Service.dll) Version: 1.0.0.0 (1.0.0.0)
Syntax
C#VBC++WebAPI
Copypublic interface ISizerService : ICompacService
Public Interface ISizerService
	Inherits ICompacService
public interface class ISizerService : ICompacService
Not applicable.
The ISizerService type exposes the following members.

Methods
   Name Description 
Public method ActivateLayout Loads the specified saved layout and makes it active (changing the active layout id). 
Public method ActivateProducts Adds Products to the Active Product List of the given Variety and Layout  
Public method AddBatch Add new batch to the end of batch queue. Invalid batch will not be added.  
Public method AddBatches Add list of batches to the end of batch queue.  
Public method AddBatchLanes Make a current Batch also run on the specified LaneGroupings (internally a Batch Change on those groupings)  
Public method AddGrower Adds a new Grower to the system.  
Public method AddGrowers Adds a list of new Growers to the system.  
Public method AddProduct Creates (but does not activate) a new product in the specified variety. 
Public method AddProductToNextAvailableOutletStrategy Adds a product to the Next Available Outlet strategy pool for the current layout. (No error occurs if the product is already in the pool.)  
Public method ChangeBatch Perform a Batch change across the whole machine  
Public method ChangeBatchByLane Perform a Batch change on specified LaneGroupings (see ChangeBatch(Batch) for general description)  
Public method ChangeProductAtOutlet Changes the active Product at an Outlet  
Public method ClearBatches Clears all batches from the machine. The exact effect of this is intentionally not specified, but in general it tries to ensure that traces of older batches are erased (although staging may be unaffected). A subsequent batch change is required in order to resume normal operation.  
Public method ClearBatchQueue Remove all batches in batch queue.  
Public method CreateLayout Creates a new Layout for the given Variety 
Public method CycleOutlet Forces an Outlet to cycle / eject a pack and reset its counts. Useful for flushing out partial packs when changing things around  
Public method DeleteGrower Deletes a Grower from the system.  
Public method DeleteLayout Deletes an existing saved layout for the given Variety 
Public method DeleteProduct Deletes an existing product. 
Public method FinishAllBatches Finish all batches (set finish time of all batches to Now and stop totaling until next batch)  
Public method FinishBatch Finish the batch (sets finish time of the batch to Now and stops totaling until next batch)  
Public method FlushTotals Flushes the totals to the database. After this call returned it can take a few seconds until all the totals are actually written.  
Public method ForceChangeOnStage The same functionality as pressing "Force Change" in the batch staging window  
Public method GetActiveLabels Gets a list of the currently active Label on each Labeler, in order. 
Public method GetActiveLayout The currently active Layout for a Variety  
Public method GetActiveProducts Get the active Products for a Variety and Layout  
Public method GetActiveVarieties Gets the current running Varieties. 
Public method GetActiveVariety Obsolete. 
Gets the current running Variety.  
Public method GetAFW Get current average fruit weight for a particular batch.  
Public method GetAllGrowers Get a list of all Growers in the system.  
Public method GetAllLabels Gets all defined Label types. Not all of these may be immediately available on the machine. 
Public method GetAllPacks Gets all defined Pack types. 
Public method GetAllProducts Get all Products for a Variety  
Public method GetAllVarieties Gets the list of all Varieties. This call potentially take a longer time depending on the number of varieties and products.  
Public method GetBatch Get a Batch  
Public method GetBatchAtOutlet Gets the active Batch at an Outlet  
Public method GetBatchesAtOutlet Gets the active Batches at an Outlet  
Public method GetBatchesForProductAtOutlet Gets the potential weighbridge batches for the product at an Outlet  
Public method GetBatchLaneGroups Get the Lane Groupings of a current batch.  
Public method GetBatchLanes Get the Lanes of a current batch.  
Public method GetCupfillForAllLanes Percentage of the carriers containing a fruit when passing the weigh bridge for all lanes.  
Public method GetCurrentBatch Obsolete. 
Get the current running Batch.  
Public method GetCurrentBatchByLane Get the current running Batch for a given lane.  
Public method GetCurrentBatchByLaneGroup Get the current batch for a given lane group  
Public method GetCurrentBatches Get the current running Batches.  
Public method GetCurrentCartonEquivalentWeight Gets the current Carton Equivalent weight in kilograms  
Public method GetExactNumberOfPackersForOutlet Where calculation of packer numbers from outlet FPM is set up This returns the exact number of packers for the designated outlet  
Public method GetExactNumberOfPackersForOutlets Where calculation of packer numbers from outlet FPM is set up This returns the exact number of packers for all outlets  
Public method GetGrower Returns the Grower with the given code.  
Public method GetLaneGradeFPM Function which gets the current delivered FPMs for each grade for that lane  
Public method GetLaneGroups Get the Groups of Lanes set up on the machine  
Public method GetLanesCupfill Percentage of the carriers containing a fruit when passing the weigh bridge for a particular lane.  
Public method GetLanesGradeFPM Function which gets the current delivered FPMs for each grade for all lanes  
Public method GetLaneSizeFPM Function which gets the current delivered FPMs for each size for that lane  
Public method GetLanesSizeFPM Function which gets the current delivered FPMs for each size for all lanes  
Public method GetLayout A Layout for a Variety  
Public method GetLayouts Get the Layouts available for the given Variety  
Public method GetMachineAFW Get current average fruit weight across the machine.  
Public method GetMachineAVC Get the current average count of fruit per Pack.  
Public method GetMachineCupfill Percentage of the carriers containing a fruit when passing the weigh bridge.  
Public method GetMachineDroppedFPM Total fruit per minute being delivered.  
Public method GetMachineMissedFPM Missed fruit per minute. This is fruit which should have been delivered to an Outlet but was detected going off the end of the machine. This usually is caused by bad solenoids or fruit jumping carriers  
Public method GetMachineName Retrieves the machine name for the Sizer instance  
Public method GetMachinePackedFPM Total fruit per minute being totaled under a summarizable totaling category.  
Public method GetMachinePacksPH Packs Per hour  
Public method GetMachineRecycleFPM The machines current recycled fruit per minute. This is fruit that is being intentionally recycled. Usually because there is no place for it to go.  
Public method GetMachineRejectFPM The machines current reject fruit per minute. This is fruit that is being totaled under a reject totaling category. Usually undersize and oversize fruit  
Public method GetMachineRodsPM The machine speed in "Rods Per Minute" for each frame  
Public method GetMachineTonnesPH Current tonnes per hour going over the machine  
Public method GetMachineTotalFPM Total incoming fruit / minute running over sizer  
Public method GetNextBatchByLaneGroup Get next pending batch for a lane group. i.e. batch started but not reached the weighbridge yet or next batch in batch queue  
Public method GetNextBatches Get next pending batch for all lane groups. i.e. batch started but not reached the weighbridge yet or next batch in batch queue  
Public method GetNextBatchInQueue Get next batch in the batch queue.  
Public method GetNumberOfPackersForOutlet Where calculation of packer numbers from outlet FPM is set up This returns the calculated number of packers for the designated outlet  
Public method GetNumberOfPackersForOutlets Where calculation of packer numbers from outlet FPM is set up This returns the calculated number of packers for all outlets  
Public method GetOutletFPM Fruit Per Minute currently being delivered at the Outlet  
Public method GetOutlets Gets all the Outlets in the system  
Public method GetOutletsFPM Fruit Per Minute currently being delivered at the Outlet  
Public method GetPackhouseName Retrieves the packhouse name for the Sizer instance  
Public method GetProduct Get the details for a Product from the current active Variety  
Public method GetProductAtOutlet Gets the active Product at an Outlet  
Public method GetProductsInNextAvailableOutletStrategy Returns the list of products in the Next Available Outlet strategy pool for the current layout.  
Public method GetProductUserData Gets the value of User data associated with a Product  
Public method GetQueuedBatches Get all batches in the batch queue.  
Public method GetSamplingConfiguration Gets the sampling configuration on an outlet  
Public method GetSamplingProgress Gets the sampling progress at the outlet  
Public method GetSerialNo Retrieves the serial number for the Sizer instance  
Public method GetSizingProfileNames Get all available Sizing Profile names  
Public method GetStagedBatches Gets the current batch staging configuration and state.  
Public method GetTotalFPMForAllLanes Total incoming fruit / minute running over sizer for each lane.  
Public method GetTotalNumberOfOutletPackers Where calculation of packer numbers from outlet FPM is set up This returns the number of packers entered in sizer for distribution between outlets  
Public method GetVarietyById Get Variety by its ID  
Public method GetVarietyByName Get Variety by its name  
Public method GetVarietyNames Get all names of the Varieties in the system  
Public method GetVersion Gets the ServiceVersion of the service. 
(Inherited from ICompacService.) 
Public method IsCurrentBatch Is the given batch id a currently running Batch?  
Public method MatchesCurrentBatch Determines if current batch id matches the specified batch id - used to identify change in batch  
Public method RaiseAlarm Used to raise an Alarm dialog box on the sizer computer  
Public method RaiseAlarmWithPriority Used to raise an Alarm dialog box on the sizer computer  
Public method RaiseEvent Raises a ServiceEvent from the ISizerEventService. Not intended for general use.  
Public method ReactivateAllBatches Reactivate all finished batches (clear finish time of all batches and start totaling again)  
Public method ReactivateBatch Reactivate the finished batch (clears finish time of the batch and starts totaling again)  
Public method RemoveNextBatchInQueue Remove next batch in batch queue.  
Public method RemoveNextBatchInQueueForLaneGroup Remove next batch at given lane group in batch queue. If next batch is for multiple stage, it will be removed if removeWholeBatch is true. Otherwise the given lane group will be removed from that batch and without remove batch from batch queue.  
Public method RemoveProduct Removes a Product from a Layout of a Variety. This will also remove the Product from all Outlets it is assigned to.  
Public method RemoveProductFromNextAvailableOutletStrategy Removes a product from the Next Available Outlet strategy pool for the current layout. (No error occurs if the product is not in the pool.)  
Public method ResetOutlet Reset the counts at an Outlet. Often useful if the pack / bin has been taken away from the Outlet  
Public method SecondsToBallArrivingOnLastBatchChange If batch change by blue ball arriving, this will give the number of seconds it took from the batch change at infeed until the ball was detected by vision  
Public method SetBatchDetails Changes the comments or other details for an existing batch. Note that all of them are always changed -- if you want to only update one of them you will need to call GetBatch or GetCurrentBatch first to retrieve the current comments and modify accordingly.  
Public method SetCurrentCartonEquivalentWeight Sets the Carton Equivalent Weight for the current variety in Kilograms.  
Public method SetOutletEnabled Enable or disable an Outlet. This affects the current layout only.  
Public method SetOutletGroup Where calculation of packer numbers from outlet FPM is set up This sets the outlet group for packers  
Public method SetOutletOutOfOrder Mark an Outlet as out-of-order, or not. This is similar to disabling it but is global.  
Public method SetOutletsMaxRateSquareCMPerMinute Changes the custom capacity rate limit at multiple outlets to the specified square cm per minute value. Using a new rate < 0 will make the outlet use the default rate limit.  
Public method SetSamplingConfiguration Sets the sampling configuration on an outlet  
Public method SetTotalNumberOfOutletPackers Where calculation of packer numbers from outlet FPM is set up This set the number of packers for distribution between outlets  
Public method StartNextBatchInQueue Start next batch in batch queue. No operation if next batch is invalid. ChangeBatch(Batch) 
Public method StartNextBatchInQueueForLaneGroup Start next batch in batch queue for given lane group.  
Public method UpdateGrower Updates an existing Grower with new information. All the information are transferred. Call GetGrower to get an existing one and change what you need if only specific details should be changed.  
Public method UpdateLayout Updates the given layout. Both saved layouts and active layouts can be updated, though use caution with the latter to avoid disrupting operators and delivering incomplete packs or recycle. 
Public method UpdateProduct Modifies the definition of an existing product. 
